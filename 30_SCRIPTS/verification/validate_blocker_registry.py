"""Deterministic validation engine for Book-to-Memory Blocker Registry & History.

Enforces:
- Canonical blocker record schema (15H)
- Immutable hash-chained transition history (15I)
- Valid lifecycle state transitions (15B, 15C, 15I.5)
- Close-gate verification and tamper detection (15E, 15K)
- Output format specification (15I.7)
- No severity decrease without an owner attestation record (B12): relative to a base version of the
  registry (`--base-ref`), any blocker whose severity drops, in the register or in OPEN_BLOCKERS.md,
  needs a matching entry in SEVERITY_ATTESTATIONS.md (typed owner principal + evidence reference).
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
from pathlib import Path
from typing import Any, Dict, List, Optional, Set, Tuple

import yaml

_REPO_ROOT = Path(__file__).resolve().parents[2]
# The severity gate lives in `security.trust_gate` (repo root) and the owner-principal rule in the
# `cognitive_core` shim under 03_IMPLEMENTATION/packages. The repo root goes first so the root
# `security` package wins over packages/security; packages is appended for everything else.
if str(_REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(_REPO_ROOT))
_PACKAGES = str(_REPO_ROOT / "03_IMPLEMENTATION" / "packages")
if _PACKAGES not in sys.path:
    sys.path.append(_PACKAGES)

from security.trust_gate import (  # noqa: E402
    SeverityAttestationError,
    SeverityDowngradeAttestation,
    is_severity_decrease,
    verify_severity_attestation,
)

DEFAULT_REGISTER = Path("08_RESEARCH/BOOK_TO_MEMORY/BLOCKER_REGISTER.md")
DEFAULT_HISTORY = Path("08_RESEARCH/BOOK_TO_MEMORY/BLOCKER_HISTORY.md")
DEFAULT_OPEN_BLOCKERS = Path("08_RESEARCH/BOOK_TO_MEMORY/OPEN_BLOCKERS.md")
DEFAULT_ATTESTATIONS = Path("08_RESEARCH/BOOK_TO_MEMORY/SEVERITY_ATTESTATIONS.md")

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


# ---------------------------------------------------------------------------
# B12: severity can only go down with an explicit owner attestation
# ---------------------------------------------------------------------------

def parse_attestation_ledger(content: str) -> Tuple[List[SeverityDowngradeAttestation], List[str]]:
    """Parse SEVERITY_ATTESTATIONS.md into typed attestations.

    Every YAML block is one attestation: attestation_id, blocker_id, from_severity, to_severity,
    principal, attested_by, evidence_ref, attested_at. The principal is turned into the vault's
    ``Principal`` enum; a value that is not a Principal stays a plain string, which the gate
    refuses. A block that does not parse as an attestation is an error, not skipped.
    """
    from cognitive_core.authorizer import Principal

    attestations: List[SeverityDowngradeAttestation] = []
    errors: List[str] = []
    fenced = re.findall(r"```ya?ml\s*\n(.*?)\n```", content, re.DOTALL | re.IGNORECASE)
    blocks = [d for chunk in fenced for d in yaml.safe_load_all(chunk) if isinstance(d, dict)]
    for idx, block in enumerate(blocks):
        label = str(block.get("attestation_id") or f"attestation[{idx}]")
        missing = [
            k for k in ("blocker_id", "from_severity", "to_severity", "principal", "attested_by", "evidence_ref", "attested_at")
            if not block.get(k)
        ]
        if missing:
            errors.append(f"{label}:attestation_missing_fields:{','.join(missing)}")
            continue
        raw_principal = str(block["principal"]).strip().lower()
        try:
            principal: Any = Principal(raw_principal)
        except ValueError:
            principal = raw_principal  # not a Principal: refused when verified
        attestation = SeverityDowngradeAttestation(
            principal=principal,
            evidence_ref=str(block["evidence_ref"]),
            attested_by=str(block["attested_by"]),
            blocker_id=str(block["blocker_id"]),
            from_severity=str(block["from_severity"]),
            to_severity=str(block["to_severity"]),
        )
        try:
            verify_severity_attestation(
                attestation, attestation.blocker_id, attestation.from_severity, attestation.to_severity
            )
        except SeverityAttestationError as exc:
            errors.append(f"{label}:invalid_severity_attestation:{exc}")
            continue
        attestations.append(attestation)
    return attestations, errors


def parse_open_blockers_severities(content: str) -> Dict[str, str]:
    """`Blocker ID -> Severity` from the summary table of OPEN_BLOCKERS.md (markup stripped)."""
    result: Dict[str, str] = {}
    in_table = False
    for line in content.splitlines():
        if not line.lstrip().startswith("|"):
            if in_table:
                break
            continue
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if not in_table:
            if len(cells) >= 2 and cells[0].lower() == "blocker id" and cells[1].lower() == "severity":
                in_table = True
            continue
        if set(line.replace("|", "").strip()) <= {"-", ":", " "}:
            continue
        ident = cells[0].strip("*` ")
        severity = cells[1].strip("*` ")
        if ident:
            result[ident] = severity
    return result


def check_severity_downgrades(
    base: Dict[str, str],
    current: Dict[str, str],
    attestations: List[SeverityDowngradeAttestation],
    source: str,
) -> List[str]:
    """Errors for every blocker whose severity fell (or that vanished) between `base` and `current`.

    A decrease passes only if an attestation for that blocker and that exact transition verifies.
    A severity label the scale does not know is refused, and so is deleting a blocker.
    """
    errors: List[str] = []
    for bid, old_sev in sorted(base.items()):
        if bid not in current:
            errors.append(f"{source}:{bid}:blocker_removed_without_attestation")
            continue
        new_sev = current[bid]
        if new_sev == old_sev:
            continue
        try:
            decreased = is_severity_decrease(old_sev, new_sev)
        except ValueError as exc:
            errors.append(f"{source}:{bid}:unknown_severity_in_change:{old_sev}->{new_sev}:{exc}")
            continue
        if not decreased:
            continue  # raising a severity is always allowed
        reasons: List[str] = []
        authorised = False
        for att in attestations:
            if att.blocker_id != bid:
                continue
            try:
                verify_severity_attestation(att, bid, old_sev, new_sev)
                authorised = True
                break
            except SeverityAttestationError as exc:
                reasons.append(str(exc))
        if not authorised:
            detail = f" ({reasons[0]})" if reasons else ""
            errors.append(f"{source}:{bid}:severity_downgrade_without_attestation:{old_sev}->{new_sev}{detail}")
    return errors


def _git_show(ref: str, rel_path: Path, repo: Path) -> Optional[str]:
    """File content at `ref`, or None if the path does not exist there. A bad ref is an error."""
    verify = subprocess.run(
        ["git", "rev-parse", "--verify", "--quiet", f"{ref}^{{commit}}"],
        cwd=repo, capture_output=True, text=True,
    )
    if verify.returncode != 0:
        raise RuntimeError(f"base ref not found: {ref}")
    spec = f"{ref}:{rel_path.as_posix()}"
    exists = subprocess.run(["git", "cat-file", "-e", spec], cwd=repo, capture_output=True, text=True)
    if exists.returncode != 0:
        return None
    shown = subprocess.run(["git", "show", spec], cwd=repo, capture_output=True, text=True, encoding="utf-8")
    if shown.returncode != 0:
        raise RuntimeError(f"cannot read {spec}: {shown.stderr.strip()}")
    return shown.stdout


def validate_registry_and_history(
    records: List[Dict[str, Any]],
    transitions: List[Dict[str, Any]],
    base_records: Optional[List[Dict[str, Any]]] = None,
    attestations: Optional[List[SeverityDowngradeAttestation]] = None,
) -> Dict[str, Any]:
    """Validate full blocker register against immutable transition history.

    With `base_records` (the register as it was on the base branch) every severity decrease must be
    covered by one of `attestations`.
    """
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

        # Severity downgrades are checked against the base register below (B12): this record alone
        # cannot show that its severity was lowered.

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

    # 4. Severity downgrade gate (B12): compare with the register on the base branch
    if base_records is not None:
        base_sev = {str(r.get("blocker_id")): str(r.get("severity")) for r in base_records if r.get("blocker_id")}
        cur_sev = {bid: str(rec.get("severity")) for bid, rec in blocker_map.items()}
        errors.extend(check_severity_downgrades(base_sev, cur_sev, attestations or [], "register"))

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


def _git_toplevel(path: Path) -> Path:
    """Root of the git work tree that contains `path`."""
    out = subprocess.run(
        ["git", "-C", str(path.resolve().parent), "rev-parse", "--show-toplevel"],
        capture_output=True, text=True,
    )
    if out.returncode != 0:
        raise RuntimeError(f"not inside a git work tree: {path}")
    return Path(out.stdout.strip())


def _repo_relative(path: Path, repo: Path) -> Path:
    return path.resolve().relative_to(repo.resolve())


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(description="Validate Book-to-Memory blocker registry and history.")
    parser.add_argument("--register", type=Path, default=DEFAULT_REGISTER)
    parser.add_argument("--history", type=Path, default=DEFAULT_HISTORY)
    parser.add_argument("--open-blockers", type=Path, default=DEFAULT_OPEN_BLOCKERS)
    parser.add_argument("--attestations", type=Path, default=DEFAULT_ATTESTATIONS)
    parser.add_argument(
        "--base-ref",
        default=None,
        help="Git ref of the branch being merged into (e.g. origin/main). When given, any severity "
        "decrease relative to that ref needs an entry in the attestation ledger (B12).",
    )
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

    # The ledger itself is always validated: an entry signed by anything but an owner principal,
    # or without evidence, is an error even when nothing relies on it.
    attestations: List[SeverityDowngradeAttestation] = []
    ledger_errors: List[str] = []
    if args.attestations.exists():
        attestations, ledger_errors = parse_attestation_ledger(args.attestations.read_text(encoding="utf-8"))

    base_records: Optional[List[Dict[str, Any]]] = None
    extra_errors: List[str] = list(ledger_errors)
    if args.base_ref:
        try:
            repo = _git_toplevel(args.register)
            base_register = _git_show(args.base_ref, _repo_relative(args.register, repo), repo)
            base_records = extract_yaml_blocks(base_register) if base_register is not None else None
            base_open = _git_show(args.base_ref, _repo_relative(args.open_blockers, repo), repo)
        except (RuntimeError, ValueError) as exc:
            print(f"BLOCKER_REGISTRY_STATUS=FAIL\nError: {exc}")
            return 1
        if base_open is not None:
            if not args.open_blockers.exists():
                extra_errors.append("open_blockers:file_removed_without_attestation")
            else:
                extra_errors.extend(
                    check_severity_downgrades(
                        parse_open_blockers_severities(base_open),
                        parse_open_blockers_severities(args.open_blockers.read_text(encoding="utf-8")),
                        attestations,
                        "open_blockers",
                    )
                )

    res = validate_registry_and_history(records, transitions, base_records=base_records, attestations=attestations)
    if extra_errors:
        res["errors"] = sorted(set(res["errors"]) | set(extra_errors))
        res["valid"] = False

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
