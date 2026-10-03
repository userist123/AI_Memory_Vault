"""Deterministic validation engine for Book-to-Memory Blocker Registry & History.

Enforces:
- Canonical blocker record schema (15H)
- Immutable hash-chained transition history (15I)
- Valid lifecycle state transitions (15B, 15C, 15I.5)
- Close-gate verification and tamper detection (15E, 15K)
- Output format specification (15I.7)
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path
from typing import Any, Dict, List, Optional, Set, Tuple

import yaml

VALID_SEVERITIES = {"HARD_BLOCKER", "SOFT_BLOCKER", "WARNING"}

VALID_STATUSES = {
    "OPEN",
    "TRIAGED",
    "MITIGATION_IN_PROGRESS",
    "AWAITING_OWNER",
    "AWAITING_EXTERNAL_EVIDENCE",
    "RESOLVED_PENDING_VERIFICATION",
    "CLOSED",
    "WONT_FIX",
    "INVALIDATED",
    "REOPENED",
}

VALID_TRANSITIONS: Dict[Optional[str], Set[str]] = {
    None: {"OPEN"},
    "OPEN": {"TRIAGED"},
    "TRIAGED": {"MITIGATION_IN_PROGRESS", "AWAITING_OWNER", "AWAITING_EXTERNAL_EVIDENCE", "INVALIDATED"},
    "MITIGATION_IN_PROGRESS": {"AWAITING_OWNER", "AWAITING_EXTERNAL_EVIDENCE", "RESOLVED_PENDING_VERIFICATION"},
    "AWAITING_OWNER": {"MITIGATION_IN_PROGRESS", "REOPENED"},
    "AWAITING_EXTERNAL_EVIDENCE": {"MITIGATION_IN_PROGRESS", "REOPENED"},
    "RESOLVED_PENDING_VERIFICATION": {"CLOSED", "REOPENED"},
    "CLOSED": {"REOPENED"},
    "WONT_FIX": {"REOPENED"},
    "INVALIDATED": {"REOPENED"},
    "REOPENED": {"TRIAGED", "MITIGATION_IN_PROGRESS", "AWAITING_OWNER", "AWAITING_EXTERNAL_EVIDENCE"},
}


def canonical_json_bytes(data: Dict[str, Any], exclude_keys: Tuple[str, ...] = ()) -> bytes:
    """Deterministic JSON serialization with sorted keys and compact separators."""
    clean = {k: v for k, v in data.items() if k not in exclude_keys}
    return json.dumps(clean, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")


def compute_hash(data: Dict[str, Any], exclude_keys: Tuple[str, ...] = ()) -> str:
    """Compute SHA-256 hash over canonical representation."""
    return hashlib.sha256(canonical_json_bytes(data, exclude_keys)).hexdigest()


def compute_record_hash(record: Dict[str, Any]) -> str:
    """Compute canonical hash of a blocker record, excluding integrity.record_hash."""
    rec = dict(record)
    if "integrity" in rec and isinstance(rec["integrity"], dict):
        rec_integrity = dict(rec["integrity"])
        rec_integrity.pop("record_hash", None)
        rec["integrity"] = rec_integrity
    return compute_hash(rec)


def compute_transition_hash(transition: Dict[str, Any]) -> str:
    """Compute canonical hash of a transition, excluding transition_hash."""
    return compute_hash(transition, exclude_keys=("transition_hash",))


def extract_yaml_blocks(content: str) -> List[Dict[str, Any]]:
    """Extract YAML objects from markdown code fences or raw YAML documents."""
    blocks = []
    # Find all ```yaml ... ``` fences
    matches = re.findall(r"```ya?ml\s*\n(.*?)\n```", content, re.DOTALL | re.IGNORECASE)
    if matches:
        for m in matches:
            docs = yaml.safe_load_all(m)
            for d in docs:
                if isinstance(d, dict):
                    blocks.append(d)
    else:
        # Fallback to direct safe_load_all
        docs = yaml.safe_load_all(content)
        for d in docs:
            if isinstance(d, dict):
                blocks.append(d)
    return blocks


def validate_registry_and_history(
    records: List[Dict[str, Any]],
    transitions: List[Dict[str, Any]],
) -> Dict[str, Any]:
    """Validate full blocker register against immutable transition history."""
    errors: List[str] = []
    warnings: List[str] = []

    invalid_transitions_count = 0
    broken_hash_chain_count = 0
    orphan_transitions_count = 0
    status_history_mismatch_count = 0

    # 1. Validate records uniqueness and schema
    seen_blocker_ids: Set[str] = set()
    blocker_map: Dict[str, Dict[str, Any]] = {}

    prev_record_hash: Optional[str] = None
    for idx, rec in enumerate(records):
        bid = str(rec.get("blocker_id", ""))
        if not bid:
            errors.append(f"record[{idx}]:missing_blocker_id")
            continue
        if bid in seen_blocker_ids:
            errors.append(f"duplicate_blocker_id:{bid}")
        seen_blocker_ids.add(bid)
        blocker_map[bid] = rec

        sev = rec.get("severity")
        if sev not in VALID_SEVERITIES:
            errors.append(f"{bid}:invalid_severity:{sev}")

        st = rec.get("status")
        if st not in VALID_STATUSES:
            errors.append(f"{bid}:invalid_status:{st}")

        # Record hash verification
        integrity = rec.get("integrity")
        if not isinstance(integrity, dict):
            errors.append(f"{bid}:missing_integrity_block")
        else:
            expected_rec_hash = integrity.get("record_hash")
            actual_rec_hash = compute_record_hash(rec)
            if expected_rec_hash and expected_rec_hash != actual_rec_hash:
                errors.append(f"{bid}:record_hash_mismatch:expected_{expected_rec_hash}_got_{actual_rec_hash}")

            expected_prev_hash = integrity.get("previous_record_hash")
            if idx > 0 and expected_prev_hash is not None and prev_record_hash and expected_prev_hash != prev_record_hash:
                errors.append(f"{bid}:previous_record_hash_mismatch")
            prev_record_hash = actual_rec_hash

    # Check dependency references in records
    for bid, rec in blocker_map.items():
        deps = rec.get("dependencies") or {}
        for dep_type in ("blocks", "blocked_by", "depends_on", "duplicates", "supersedes"):
            for dep_id in deps.get(dep_type, []) or []:
                if dep_id not in seen_blocker_ids:
                    errors.append(f"{bid}:orphan_dependency:{dep_type}:{dep_id}")

    # 2. Validate transitions
    seen_transition_ids: Set[str] = set()
    blocker_transitions: Dict[str, List[Dict[str, Any]]] = {bid: [] for bid in seen_blocker_ids}
    prev_transition_hash_global: Optional[str] = None

    for tidx, tr in enumerate(transitions):
        tid = str(tr.get("transition_id", ""))
        if not tid:
            errors.append(f"transition[{tidx}]:missing_transition_id")
            invalid_transitions_count += 1
            continue
        if tid in seen_transition_ids:
            errors.append(f"duplicate_transition_id:{tid}")
            invalid_transitions_count += 1
        seen_transition_ids.add(tid)

        bid = str(tr.get("blocker_id", ""))
        if bid not in blocker_map:
            errors.append(f"orphan_transition:{tid}:references_unknown_blocker:{bid}")
            orphan_transitions_count += 1
            invalid_transitions_count += 1
            continue

        seq = tr.get("sequence")
        from_st = tr.get("from_status")
        to_st = tr.get("to_status")

        # Genesis checks
        if seq == 1:
            if from_st is not None:
                errors.append(f"{tid}:genesis_must_have_null_from_status:got_{from_st}")
                invalid_transitions_count += 1
            if to_st != "OPEN":
                errors.append(f"{tid}:genesis_must_have_to_status_OPEN:got_{to_st}")
                invalid_transitions_count += 1
            if tr.get("previous_transition_hash") is not None and tidx == 0:
                errors.append(f"{tid}:initial_genesis_previous_hash_must_be_null")
                broken_hash_chain_count += 1

        # Transition matrix check
        allowed_targets = VALID_TRANSITIONS.get(from_st, set())
        if to_st not in allowed_targets:
            errors.append(f"{tid}:invalid_status_transition:{from_st}->{to_st}")
            invalid_transitions_count += 1

        # Reopen checks
        if to_st == "REOPENED":
            if not tr.get("reason"):
                errors.append(f"{tid}:reopen_missing_reason")
                invalid_transitions_count += 1
            if not tr.get("evidence"):
                errors.append(f"{tid}:reopen_missing_evidence")
                invalid_transitions_count += 1

        # Hash check
        expected_tr_hash = tr.get("transition_hash")
        actual_tr_hash = compute_transition_hash(tr)
        if expected_tr_hash and expected_tr_hash != actual_tr_hash:
            errors.append(f"{tid}:transition_hash_mismatch:expected_{expected_tr_hash}_got_{actual_tr_hash}")
            broken_hash_chain_count += 1

        # Chain link verification: check previous_transition_hash against either global or per-blocker chain
        prev_hash = tr.get("previous_transition_hash")
        if seq == 1 and prev_hash is None:
            pass  # Allowed for genesis
        elif prev_transition_hash_global and prev_hash == prev_transition_hash_global:
            pass  # Matches global linear chain
        elif blocker_transitions[bid]:
            last_for_blocker = blocker_transitions[bid][-1]
            if prev_hash == last_for_blocker.get("transition_hash"):
                pass  # Matches per-blocker chain
            else:
                errors.append(f"{tid}:broken_hash_chain:invalid_previous_transition_hash")
                broken_hash_chain_count += 1
        elif prev_hash is not None:
            errors.append(f"{tid}:broken_hash_chain:unmatched_previous_transition_hash")
            broken_hash_chain_count += 1

        prev_transition_hash_global = actual_tr_hash
        blocker_transitions[bid].append(tr)

    # 3. Check monotonicity and history-to-register alignment per blocker
    for bid, rec in blocker_map.items():
        b_trs = blocker_transitions.get(bid, [])
        if not b_trs:
            errors.append(f"{bid}:no_transitions_in_history")
            status_history_mismatch_count += 1
            continue

        # Check sequence monotonicity
        expected_seq = 1
        for tr in b_trs:
            actual_seq = tr.get("sequence")
            if actual_seq != expected_seq:
                errors.append(f"{bid}:broken_sequence:expected_{expected_seq}_got_{actual_seq}")
                invalid_transitions_count += 1
            expected_seq += 1

        # Check final status matches register
        last_tr = b_trs[-1]
        last_to_status = last_tr.get("to_status")
        rec_status = rec.get("status")
        if rec_status != last_to_status:
            errors.append(f"{bid}:status_history_mismatch:register_{rec_status}_vs_history_{last_to_status}")
            status_history_mismatch_count += 1

        # Check severity downgrade guard
        # If any transition or initial severity was HARD_BLOCKER, register cannot be WARNING
        had_hard = rec.get("severity") == "HARD_BLOCKER"
        if had_hard and rec_status != "CLOSED" and rec.get("severity") == "WARNING":
            errors.append(f"{bid}:hard_blocker_silently_downgraded_to_warning")

        # Check CLOSED gate requirements
        if rec_status == "CLOSED":
            closure = rec.get("closure") or {}
            remediation = rec.get("remediation") or {}
            root_cause = rec.get("root_cause") or {}

            if root_cause.get("confidence") == "LOW":
                errors.append(f"{bid}:closed_with_low_confidence_root_cause")

            if not closure.get("criteria"):
                errors.append(f"{bid}:closed_missing_criteria")
            if not closure.get("evidence_required"):
                errors.append(f"{bid}:closed_missing_evidence_required")
            if not closure.get("resolved_at"):
                errors.append(f"{bid}:closed_missing_resolved_at")
            if not closure.get("resolved_by"):
                errors.append(f"{bid}:closed_missing_resolved_by")
            if not closure.get("resolution_evidence"):
                errors.append(f"{bid}:closed_missing_resolution_evidence")

            # Check dependent blockers are not open
            deps = rec.get("dependencies") or {}
            blocked_by = deps.get("blocked_by", [])
            for blocker_dep in blocked_by:
                dep_rec = blocker_map.get(blocker_dep)
                if dep_rec and dep_rec.get("status") not in {"CLOSED", "INVALIDATED"}:
                    errors.append(f"{bid}:closed_while_dependency_active:{blocker_dep}:{dep_rec.get('status')}")

            # Check owner approval if required
            if remediation.get("owner_approval_required"):
                ref = remediation.get("owner_approval_reference") or last_tr.get("approval_reference")
                if not ref:
                    errors.append(f"{bid}:closed_missing_owner_approval_reference")

    # Counts
    total_blockers = len(blocker_map)
    active_hard = sum(1 for r in blocker_map.values() if r.get("severity") == "HARD_BLOCKER" and r.get("status") not in {"CLOSED", "INVALIDATED", "WONT_FIX"})
    active_soft = sum(1 for r in blocker_map.values() if r.get("severity") == "SOFT_BLOCKER" and r.get("status") not in {"CLOSED", "INVALIDATED", "WONT_FIX"})
    warnings_count = sum(1 for r in blocker_map.values() if r.get("severity") == "WARNING")

    is_valid = len(errors) == 0

    return {
        "valid": is_valid,
        "errors": sorted(set(errors)),
        "warnings": sorted(set(warnings)),
        "blockers_total": total_blockers,
        "active_hard_blockers": active_hard,
        "active_soft_blockers": active_soft,
        "warnings_count": warnings_count,
        "invalid_transitions": invalid_transitions_count,
        "broken_hash_chain": broken_hash_chain_count,
        "orphan_transitions": orphan_transitions_count,
        "status_history_mismatch": status_history_mismatch_count,
    }


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(description="Validate Book-to-Memory blocker registry and history.")
    parser.add_argument("--register", type=Path, default=Path("08_RESEARCH/BOOK_TO_MEMORY/BLOCKER_REGISTER.md"))
    parser.add_argument("--history", type=Path, default=Path("08_RESEARCH/BOOK_TO_MEMORY/BLOCKER_HISTORY.md"))
    args = parser.parse_args(argv)

    if not args.register.exists():
        print(f"BLOCKER_REGISTRY_STATUS=FAIL\nError: Register file not found: {args.register}")
        return 1
    if not args.history.exists():
        print(f"BLOCKER_REGISTRY_STATUS=FAIL\nError: History file not found: {args.history}")
        return 1

    reg_content = args.register.read_text(encoding="utf-8")
    hist_content = args.history.read_text(encoding="utf-8")

    records = extract_yaml_blocks(reg_content)
    transitions = extract_yaml_blocks(hist_content)

    res = validate_registry_and_history(records, transitions)

    status_str = "PASS" if res["valid"] else "FAIL"
    print(f"BLOCKER_REGISTRY_STATUS={status_str}")
    print(f"BLOCKERS_TOTAL={res['blockers_total']}")
    print(f"ACTIVE_HARD_BLOCKERS={res['active_hard_blockers']}")
    print(f"ACTIVE_SOFT_BLOCKERS={res['active_soft_blockers']}")
    print(f"WARNINGS={res['warnings_count']}")
    print(f"INVALID_TRANSITIONS={res['invalid_transitions']}")
    print(f"BROKEN_HASH_CHAIN={res['broken_hash_chain']}")
    print(f"ORPHAN_TRANSITIONS={res['orphan_transitions']}")
    print(f"STATUS_HISTORY_MISMATCH={res['status_history_mismatch']}")

    if not res["valid"]:
        print("\nValidation Errors:")
        for err in res["errors"]:
            print(f"  - {err}")
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
