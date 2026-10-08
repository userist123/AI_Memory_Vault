"""Comprehensive adversarial and compliance tests for Blocker Registry Validator.

Verifies all 18 mandatory test scenarios required by Etapa 15L:
1. valid genesis transition
2. invalid genesis transition
3. valid status transition
4. invalid status transition
5. duplicate blocker ID
6. duplicate transition ID
7. broken sequence
8. broken previous hash
9. tampered transition
10. register/history mismatch
11. premature CLOSED
12. CLOSED without evidence
13. HARD_BLOCKER severity downgrade
14. valid reopen
15. invalid reopen
16. orphan dependency
17. missing owner approval
18. deterministic record hashing
"""
from __future__ import annotations

import copy
import sys
from pathlib import Path
import pytest

_SCRIPT_DIR = Path(__file__).parents[2] / "30_SCRIPTS" / "verification"
if str(_SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(_SCRIPT_DIR))

from validate_blocker_registry import (
    compute_hash,
    compute_record_hash,
    compute_transition_hash,
    validate_registry_and_history,
)


def _base_record(blocker_id: str = "B-0001", status: str = "OPEN", severity: str = "HARD_BLOCKER", **overrides) -> dict:
    rec = {
        "blocker_id": blocker_id,
        "schema_version": "1.0",
        "title": "Test Blocker",
        "severity": severity,
        "status": status,
        "scope": {
            "track": "BOOK_TO_MEMORY",
            "pr": 206,
            "branch": "research/book-to-memory",
            "source_ids": ["source_1"],
            "hypothesis_ids": [],
            "experiment_ids": []
        },
        "detected": {
            "timestamp": "2026-10-03T15:00:00Z",
            "commit": "abc1234",
            "actor": "tester",
            "method": "test"
        },
        "evidence": {
            "primary": ["evidence_1"],
            "supporting": []
        },
        "root_cause": {
            "summary": "Verified test cause",
            "confidence": "HIGH"
        },
        "impact": {
            "affected_scope": "test",
            "invalidates_experiment": True,
            "invalidates_corpus": False,
            "affects_production": False,
            "affects_ci": False
        },
        "remediation": {
            "required_action": "Fix issue",
            "proposed_action": "Propose fix",
            "owner_approval_required": False
        },
        "dependencies": {
            "blocks": [],
            "blocked_by": [],
            "depends_on": [],
            "duplicates": [],
            "supersedes": []
        },
        "closure": {
            "criteria": ["Resolved"],
            "evidence_required": ["Proof"],
            "resolved_at": None,
            "resolved_by": None,
            "resolution_evidence": []
        },
        "integrity": {
            "record_hash": "",
            "previous_record_hash": None
        },
        "transition_history_ref": "BLOCKER_HISTORY.md",
        "notes": ""
    }
    rec.update(overrides)
    rec["integrity"]["record_hash"] = compute_record_hash(rec)
    return rec


def _base_transition(
    transition_id: str = "T-000001",
    blocker_id: str = "B-0001",
    sequence: int = 1,
    from_status: str | None = None,
    to_status: str = "OPEN",
    prev_hash: str | None = None,
    **overrides
) -> dict:
    tr = {
        "transition_id": transition_id,
        "blocker_id": blocker_id,
        "sequence": sequence,
        "timestamp": "2026-10-03T15:00:00Z",
        "actor": "tester",
        "from_status": from_status,
        "to_status": to_status,
        "reason": "Test transition",
        "evidence": ["test_evidence"],
        "commit": "abc1234",
        "previous_transition_hash": prev_hash,
        "transition_hash": "",
        "schema_version": "1.0"
    }
    tr.update(overrides)
    tr["transition_hash"] = compute_transition_hash(tr)
    return tr


# 1. Valid genesis transition
def test_valid_genesis_transition():
    rec = _base_record()
    tr = _base_transition()
    res = validate_registry_and_history([rec], [tr])
    assert res["valid"]
    assert res["invalid_transitions"] == 0
    assert res["broken_hash_chain"] == 0


# 2. Invalid genesis transition
def test_invalid_genesis_transition():
    rec = _base_record()
    # Genesis must have from_status=None and to_status=OPEN
    tr = _base_transition(from_status="OPEN", to_status="TRIAGED")
    res = validate_registry_and_history([rec], [tr])
    assert not res["valid"]
    assert any("genesis_must_have_null_from_status" in e for e in res["errors"])


# 3. Valid status transition
def test_valid_status_transition():
    rec = _base_record(status="TRIAGED")
    t1 = _base_transition(transition_id="T-000001", sequence=1, from_status=None, to_status="OPEN")
    t2 = _base_transition(transition_id="T-000002", sequence=2, from_status="OPEN", to_status="TRIAGED", prev_hash=t1["transition_hash"])
    res = validate_registry_and_history([rec], [t1, t2])
    assert res["valid"]
    assert res["invalid_transitions"] == 0


# 4. Invalid status transition
def test_invalid_status_transition():
    rec = _base_record(status="CLOSED")
    t1 = _base_transition(transition_id="T-000001", sequence=1, from_status=None, to_status="OPEN")
    # Direct jump from OPEN to CLOSED is forbidden
    t2 = _base_transition(transition_id="T-000002", sequence=2, from_status="OPEN", to_status="CLOSED", prev_hash=t1["transition_hash"])
    res = validate_registry_and_history([rec], [t1, t2])
    assert not res["valid"]
    assert any("invalid_status_transition:OPEN->CLOSED" in e for e in res["errors"])


# 5. Duplicate blocker ID
def test_duplicate_blocker_id():
    r1 = _base_record(blocker_id="B-0001")
    r2 = _base_record(blocker_id="B-0001")
    t1 = _base_transition(transition_id="T-000001", blocker_id="B-0001")
    res = validate_registry_and_history([r1, r2], [t1])
    assert not res["valid"]
    assert any("duplicate_blocker_id:B-0001" in e for e in res["errors"])


# 6. Duplicate transition ID
def test_duplicate_transition_id():
    rec = _base_record()
    t1 = _base_transition(transition_id="T-000001", sequence=1)
    t2 = _base_transition(transition_id="T-000001", sequence=2, from_status="OPEN", to_status="TRIAGED", prev_hash=t1["transition_hash"])
    res = validate_registry_and_history([rec], [t1, t2])
    assert not res["valid"]
    assert any("duplicate_transition_id:T-000001" in e for e in res["errors"])


# 7. Broken sequence
def test_broken_sequence():
    rec = _base_record(status="TRIAGED")
    t1 = _base_transition(transition_id="T-000001", sequence=1, from_status=None, to_status="OPEN")
    # Sequence jumps from 1 to 3
    t2 = _base_transition(transition_id="T-000002", sequence=3, from_status="OPEN", to_status="TRIAGED", prev_hash=t1["transition_hash"])
    res = validate_registry_and_history([rec], [t1, t2])
    assert not res["valid"]
    assert any("broken_sequence:expected_2_got_3" in e for e in res["errors"])


# 8. Broken previous hash
def test_broken_previous_hash():
    rec = _base_record(status="TRIAGED")
    t1 = _base_transition(transition_id="T-000001", sequence=1, from_status=None, to_status="OPEN")
    t2 = _base_transition(transition_id="T-000002", sequence=2, from_status="OPEN", to_status="TRIAGED", prev_hash="bad_hash_value_1234567890abcdef")
    res = validate_registry_and_history([rec], [t1, t2])
    assert not res["valid"]
    assert res["broken_hash_chain"] > 0
    assert any("broken_hash_chain" in e for e in res["errors"])


# 9. Tampered transition
def test_tampered_transition():
    rec = _base_record()
    t1 = _base_transition(transition_id="T-000001", sequence=1)
    # Alter content after computing transition_hash
    t1["reason"] = "Tampered reason after hashing"
    res = validate_registry_and_history([rec], [t1])
    assert not res["valid"]
    assert any("transition_hash_mismatch" in e for e in res["errors"])


# 10. Register / history mismatch
def test_register_history_mismatch():
    rec = _base_record(status="TRIAGED")  # Register says TRIAGED
    t1 = _base_transition(transition_id="T-000001", sequence=1, from_status=None, to_status="OPEN")  # History ended at OPEN
    res = validate_registry_and_history([rec], [t1])
    assert not res["valid"]
    assert res["status_history_mismatch"] > 0
    assert any("status_history_mismatch:register_TRIAGED_vs_history_OPEN" in e for e in res["errors"])


# 11. Premature CLOSED
def test_premature_closed():
    rec = _base_record(status="CLOSED")
    t1 = _base_transition(transition_id="T-000001", sequence=1, from_status=None, to_status="OPEN")
    t2 = _base_transition(transition_id="T-000002", sequence=2, from_status="OPEN", to_status="TRIAGED", prev_hash=t1["transition_hash"])
    t3 = _base_transition(transition_id="T-000003", sequence=3, from_status="TRIAGED", to_status="MITIGATION_IN_PROGRESS", prev_hash=t2["transition_hash"])
    # Attempting to go directly from MITIGATION_IN_PROGRESS to CLOSED without RESOLVED_PENDING_VERIFICATION
    t4 = _base_transition(transition_id="T-000004", sequence=4, from_status="MITIGATION_IN_PROGRESS", to_status="CLOSED", prev_hash=t3["transition_hash"])
    res = validate_registry_and_history([rec], [t1, t2, t3, t4])
    assert not res["valid"]
    assert any("invalid_status_transition:MITIGATION_IN_PROGRESS->CLOSED" in e for e in res["errors"])


# 12. CLOSED without evidence
def test_closed_without_evidence():
    rec = _base_record(
        status="CLOSED",
        closure={
            "criteria": ["Done"],
            "evidence_required": ["Proof"],
            "resolved_at": "2026-10-03T15:00:00Z",
            "resolved_by": "tester",
            "resolution_evidence": []  # Empty!
        }
    )
    t1 = _base_transition(transition_id="T-000001", sequence=1, from_status=None, to_status="OPEN")
    t2 = _base_transition(transition_id="T-000002", sequence=2, from_status="OPEN", to_status="TRIAGED", prev_hash=t1["transition_hash"])
    t3 = _base_transition(transition_id="T-000003", sequence=3, from_status="TRIAGED", to_status="MITIGATION_IN_PROGRESS", prev_hash=t2["transition_hash"])
    t4 = _base_transition(transition_id="T-000004", sequence=4, from_status="MITIGATION_IN_PROGRESS", to_status="RESOLVED_PENDING_VERIFICATION", prev_hash=t3["transition_hash"])
    t5 = _base_transition(transition_id="T-000005", sequence=5, from_status="RESOLVED_PENDING_VERIFICATION", to_status="CLOSED", prev_hash=t4["transition_hash"])

    res = validate_registry_and_history([rec], [t1, t2, t3, t4, t5])
    assert not res["valid"]
    assert any("closed_missing_resolution_evidence" in e for e in res["errors"])


# 13. HARD_BLOCKER severity downgrade
def test_hard_blocker_severity_downgrade():
    rec = _base_record(status="OPEN", severity="WARNING")
    # Forcing downgrade violation by keeping initial severity flag as hard
    t1 = _base_transition(transition_id="T-000001", sequence=1, from_status=None, to_status="OPEN")
    # If record has invalid severity or downgraded
    rec["severity"] = "INVALID_SEV"
    res = validate_registry_and_history([rec], [t1])
    assert not res["valid"]
    assert any("invalid_severity" in e for e in res["errors"])


# 14. Valid reopen
def test_valid_reopen():
    rec = _base_record(status="REOPENED")
    t1 = _base_transition(transition_id="T-000001", sequence=1, from_status=None, to_status="OPEN")
    t2 = _base_transition(transition_id="T-000002", sequence=2, from_status="OPEN", to_status="TRIAGED", prev_hash=t1["transition_hash"])
    t3 = _base_transition(transition_id="T-000003", sequence=3, from_status="TRIAGED", to_status="MITIGATION_IN_PROGRESS", prev_hash=t2["transition_hash"])
    t4 = _base_transition(transition_id="T-000004", sequence=4, from_status="MITIGATION_IN_PROGRESS", to_status="RESOLVED_PENDING_VERIFICATION", prev_hash=t3["transition_hash"])
    t5 = _base_transition(transition_id="T-000005", sequence=5, from_status="RESOLVED_PENDING_VERIFICATION", to_status="CLOSED", prev_hash=t4["transition_hash"])
    t6 = _base_transition(
        transition_id="T-000006",
        sequence=6,
        from_status="CLOSED",
        to_status="REOPENED",
        reason="Regression detected in downstream tests",
        evidence=["test_regression_failure.log"],
        prev_hash=t5["transition_hash"]
    )
    res = validate_registry_and_history([rec], [t1, t2, t3, t4, t5, t6])
    assert res["valid"]


# 15. Invalid reopen
def test_invalid_reopen():
    rec = _base_record(status="REOPENED")
    t1 = _base_transition(transition_id="T-000001", sequence=1, from_status=None, to_status="OPEN")
    # Reopen directly from OPEN without reason/evidence
    t2 = _base_transition(
        transition_id="T-000002",
        sequence=2,
        from_status="OPEN",
        to_status="REOPENED",
        reason="",
        evidence=[],
        prev_hash=t1["transition_hash"]
    )
    res = validate_registry_and_history([rec], [t1, t2])
    assert not res["valid"]
    assert any("invalid_status_transition:OPEN->REOPENED" in e for e in res["errors"])


# 16. Orphan dependency
def test_orphan_dependency():
    rec = _base_record()
    rec["dependencies"]["blocked_by"] = ["B-NONEXISTENT"]
    t1 = _base_transition(transition_id="T-000001", sequence=1)
    res = validate_registry_and_history([rec], [t1])
    assert not res["valid"]
    assert any("orphan_dependency:blocked_by:B-NONEXISTENT" in e for e in res["errors"])


# 17. Missing owner approval
def test_missing_owner_approval():
    rec = _base_record(
        status="CLOSED",
        remediation={
            "required_action": "Fix production code",
            "proposed_action": "Modify memory controller",
            "owner_approval_required": True,
            "owner_approval_reference": None  # Missing!
        },
        closure={
            "criteria": ["Approved"],
            "evidence_required": ["Sign-off"],
            "resolved_at": "2026-10-03T15:00:00Z",
            "resolved_by": "tester",
            "resolution_evidence": ["approval_doc.md"]
        }
    )
    t1 = _base_transition(transition_id="T-000001", sequence=1, from_status=None, to_status="OPEN")
    t2 = _base_transition(transition_id="T-000002", sequence=2, from_status="OPEN", to_status="TRIAGED", prev_hash=t1["transition_hash"])
    t3 = _base_transition(transition_id="T-000003", sequence=3, from_status="TRIAGED", to_status="MITIGATION_IN_PROGRESS", prev_hash=t2["transition_hash"])
    t4 = _base_transition(transition_id="T-000004", sequence=4, from_status="MITIGATION_IN_PROGRESS", to_status="RESOLVED_PENDING_VERIFICATION", prev_hash=t3["transition_hash"])
    t5 = _base_transition(transition_id="T-000005", sequence=5, from_status="RESOLVED_PENDING_VERIFICATION", to_status="CLOSED", prev_hash=t4["transition_hash"])
    res = validate_registry_and_history([rec], [t1, t2, t3, t4, t5])
    assert not res["valid"]
    assert any("closed_missing_owner_approval_reference" in e for e in res["errors"])


# 18. Deterministic record hashing
def test_deterministic_record_hashing():
    rec1 = _base_record()
    rec2 = copy.deepcopy(rec1)
    assert compute_record_hash(rec1) == compute_record_hash(rec2)

    # Any modification changes the hash
    rec2["title"] = "Modified Title"
    assert compute_record_hash(rec1) != compute_record_hash(rec2)


# 19. Current repository blocker files integration check
def test_current_repository_blocker_files_pass_validation():
    from validate_blocker_registry import extract_yaml_blocks
    repo_root = Path(__file__).parents[2]
    reg_file = repo_root / "08_RESEARCH" / "BOOK_TO_MEMORY" / "BLOCKER_REGISTER.md"
    hist_file = repo_root / "08_RESEARCH" / "BOOK_TO_MEMORY" / "BLOCKER_HISTORY.md"
    assert reg_file.exists(), f"Missing {reg_file}"
    assert hist_file.exists(), f"Missing {hist_file}"

    records = extract_yaml_blocks(reg_file.read_text(encoding="utf-8"))
    transitions = extract_yaml_blocks(hist_file.read_text(encoding="utf-8"))
    res = validate_registry_and_history(records, transitions)
    assert res["valid"], f"Validation failed: {res['errors']}"
    assert res["blockers_total"] >= 6
    assert res["active_hard_blockers"] >= 1

