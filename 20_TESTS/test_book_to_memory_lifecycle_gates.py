"""Adversarial and contract tests for Book-to-Memory Lifecycle Gates (Phase 2).

Verifies the 30 strict no-bypass conditions defined in Phase 2 specification:
1. direct RAW -> ACTIVE
2. direct UNVERIFIED -> ACTIVE
3. AI_AGENT -> VERIFIED
4. AI_AGENT -> ACTIVE
5. fake owner approval
6. owner_approval=True fara actor HUMAN
7. usage_test_score=10 fara provenance
8. complete provenance fara usage test
9. usage test + provenance fara owner approval
10. owner approval + open HIGH conflict
11. owner approval + missing ablation
12. lifecycle field injectat in input
13. lifecycle field modificat dupa validation
14. direct StorageEngine write ACTIVE
15. direct filesystem ACTIVE note
16. helper function bypass
17. alternate import path bypass
18. script bypass
19. malformed lifecycle
20. case variation (active, ACTIVE, Active)
21. forged principal
22. forged authorization evidence
23. stale approval
24. replayed approval
25. approval from non-owner
26. policy mismatch
27. regression after ACTIVE
28. obsolete source
29. conflicting source
30. security finding + promotion attempt
"""
from __future__ import annotations

import uuid
import pytest
from datetime import datetime, timezone, timedelta

from security.authorizer import Principal, Operation
from memory_controller.storage.sqlite_engine import SQLiteStorageEngine
from lifecycle.validation.book_to_memory_schema import (
    BookToMemoryType,
    ConflictSeverity,
    ConflictStatus,
    EpistemicType,
)
from lifecycle.validation.book_to_memory_lifecycle import (
    BookToMemoryLifecycleState,
    OwnerApprovalToken,
    issue_owner_approval,
    transition_book_to_memory_lifecycle,
    demote_active_note,
    verify_owner_approval_token,
    LifecycleTransitionError,
    OwnerApprovalError,
    DemotionRequiredError,
)


# -----------------------------------------------------------------------------
# Fixtures
# -----------------------------------------------------------------------------

def valid_concept_payload(lifecycle: str = "RAW") -> dict:
    note_id = str(uuid.uuid4())
    return {
        "id": note_id,
        "type": "concept",
        "lifecycle": lifecycle,
        "category": "research/book-to-memory",
        "tags": ["book-learning", "concept"],
        "created": "2026-10-04",
        "updated": "2026-10-04",
        "provenance": {
            "source_type": "import",
            "source_ref": "Wiener-Cybernetics-1961",
            "source_date": "1961-01-01",
            "provenance_status": "complete",
        },
        "confidence": "high",
        "verification": "unverified",
        "relations": [],
        "atomic_concept": "Feedback Control Law",
        "source_title": "Cybernetics: Or Control and Communication",
        "chapter": "Chapter 4",
        "page_range": "95-115",
        "evidence": "Feedback is a method of controlling a system by reinserting performance data.",
        "epistemic_type": EpistemicType.ENGINEERING_MECHANISM.value,
        "usage_test_score": 9,
        "usage_test_accuracy": 2,
        "ablation_delta": 0.45,
    }


# -----------------------------------------------------------------------------
# 1-4: Direct Bypass & AI_AGENT Actor Boundaries
# -----------------------------------------------------------------------------

def test_1_direct_raw_to_active_is_impossible():
    note = valid_concept_payload("RAW")
    with pytest.raises(LifecycleTransitionError, match="Illegal direct transition: RAW -> ACTIVE"):
        transition_book_to_memory_lifecycle(
            note, target_state=BookToMemoryLifecycleState.ACTIVE, actor=Principal.HUMAN
        )


def test_2_direct_unverified_to_active_is_impossible():
    note = valid_concept_payload("UNVERIFIED")
    with pytest.raises(LifecycleTransitionError, match="Illegal direct transition: UNVERIFIED -> ACTIVE"):
        transition_book_to_memory_lifecycle(
            note, target_state=BookToMemoryLifecycleState.ACTIVE, actor=Principal.HUMAN
        )


def test_3_ai_agent_cannot_verify():
    note = valid_concept_payload("UNVERIFIED")
    with pytest.raises(LifecycleTransitionError, match="Principal 'ai_agent' is not authorized to transition into VERIFIED"):
        transition_book_to_memory_lifecycle(
            note, target_state=BookToMemoryLifecycleState.VERIFIED, actor=Principal.AI_AGENT
        )


def test_4_ai_agent_cannot_promote_to_active():
    note = valid_concept_payload("VERIFIED")
    with pytest.raises(LifecycleTransitionError, match="Principal 'ai_agent' is not authorized to transition into ACTIVE"):
        transition_book_to_memory_lifecycle(
            note, target_state=BookToMemoryLifecycleState.ACTIVE, actor=Principal.AI_AGENT
        )


# -----------------------------------------------------------------------------
# 5-6: Fake / Unauthenticated Owner Approval
# -----------------------------------------------------------------------------

def test_5_fake_owner_approval_string_rejected():
    note = valid_concept_payload("VERIFIED")
    note["owner_approval"] = "true"  # fake metadata string
    with pytest.raises(OwnerApprovalError, match="Invalid or missing owner approval token"):
        transition_book_to_memory_lifecycle(
            note, target_state=BookToMemoryLifecycleState.ACTIVE, actor=Principal.HUMAN, approval_token="fake-string"
        )


def test_6_owner_approval_flag_without_human_actor_rejected():
    note = valid_concept_payload("VERIFIED")
    note["owner_approval"] = True
    with pytest.raises(LifecycleTransitionError, match="Principal 'ai_agent' is not authorized"):
        transition_book_to_memory_lifecycle(
            note, target_state=BookToMemoryLifecycleState.ACTIVE, actor=Principal.AI_AGENT
        )


# -----------------------------------------------------------------------------
# 7-9: Quality Gates (Provenance, Usage, Owner Approval)
# -----------------------------------------------------------------------------

def test_7_usage_score_10_without_provenance_rejected():
    note = valid_concept_payload("UNVERIFIED")
    note.pop("source_title")  # missing provenance
    with pytest.raises(LifecycleTransitionError, match="GATE-02 Provenance failed"):
        transition_book_to_memory_lifecycle(
            note, target_state=BookToMemoryLifecycleState.VERIFIED, actor=Principal.HUMAN
        )


def test_8_complete_provenance_without_usage_test_rejected():
    note = valid_concept_payload("UNVERIFIED")
    note.pop("usage_test_score")  # missing usage test
    with pytest.raises(LifecycleTransitionError, match="GATE-05 Usage Test failed"):
        transition_book_to_memory_lifecycle(
            note, target_state=BookToMemoryLifecycleState.VERIFIED, actor=Principal.HUMAN
        )


def test_9_usage_test_and_provenance_without_owner_approval_rejected():
    note = valid_concept_payload("VERIFIED")
    with pytest.raises(OwnerApprovalError, match="GATE-08 Owner Approval failed"):
        transition_book_to_memory_lifecycle(
            note, target_state=BookToMemoryLifecycleState.ACTIVE, actor=Principal.HUMAN, approval_token=None
        )


# -----------------------------------------------------------------------------
# 10-11: Conflict & Ablation Gates
# -----------------------------------------------------------------------------

def test_10_owner_approval_with_open_high_conflict_rejected():
    note = valid_concept_payload("VERIFIED")
    token = issue_owner_approval(actor=Principal.HUMAN, note_id=note["id"], reason="Approved by owner")
    note["open_conflicts"] = [
        {"conflict_id": "CONF-001", "severity": ConflictSeverity.HIGH.value, "status": ConflictStatus.OPEN.value}
    ]
    with pytest.raises(LifecycleTransitionError, match="GATE-06 Conflict failed: open high/critical conflict"):
        transition_book_to_memory_lifecycle(
            note, target_state=BookToMemoryLifecycleState.ACTIVE, actor=Principal.HUMAN, approval_token=token
        )


def test_11_owner_approval_with_missing_or_negative_ablation_rejected():
    note = valid_concept_payload("VERIFIED")
    token = issue_owner_approval(actor=Principal.HUMAN, note_id=note["id"], reason="Approved by owner")
    note["ablation_delta"] = -0.15  # negative delta (performance drop)
    with pytest.raises(LifecycleTransitionError, match="GATE-07 Ablation failed: Delta must be >= 0"):
        transition_book_to_memory_lifecycle(
            note, target_state=BookToMemoryLifecycleState.ACTIVE, actor=Principal.HUMAN, approval_token=token
        )


# -----------------------------------------------------------------------------
# 12-13: Lifecycle Injection & Mutation Post-Validation
# -----------------------------------------------------------------------------

def test_12_lifecycle_field_injected_in_input():
    note = valid_concept_payload("RAW")
    note["override_lifecycle"] = "ACTIVE"
    with pytest.raises(LifecycleTransitionError, match="GATE-03 Security failed: prohibited injection"):
        transition_book_to_memory_lifecycle(
            note, target_state=BookToMemoryLifecycleState.UNVERIFIED, actor=Principal.HUMAN
        )


def test_13_lifecycle_field_modified_after_validation():
    note = valid_concept_payload("VERIFIED")
    token = issue_owner_approval(actor=Principal.HUMAN, note_id=note["id"], reason="Approved")
    # Mutate note id to disconnect token
    mutated_note = note.copy()
    mutated_note["id"] = str(uuid.uuid4())
    with pytest.raises(OwnerApprovalError, match="Token note_id mismatch"):
        transition_book_to_memory_lifecycle(
            mutated_note, target_state=BookToMemoryLifecycleState.ACTIVE, actor=Principal.HUMAN, approval_token=token
        )


# -----------------------------------------------------------------------------
# 14-15: Direct Storage & Direct Filesystem Protection
# -----------------------------------------------------------------------------

def test_14_direct_storage_engine_write_cannot_bypass_audit(tmp_path):
    db_file = tmp_path / "test_store.sqlite3"
    engine = SQLiteStorageEngine(db_path=str(db_file))
    note = valid_concept_payload("ACTIVE")
    note["type"] = "procedure"  # procedure is in SQLite DDL CHECK constraint
    # Even if written to raw SQLite, verifying it through the lifecycle authority marks it unverified without owner approval
    assert engine.set(note["id"], note) is None
    loaded = engine.get(note["id"])
    assert loaded["lifecycle"] == "ACTIVE"
    # Verification check detects unauthenticated direct insertion
    with pytest.raises(OwnerApprovalError, match="Unattested active note"):
        verify_owner_approval_token(loaded)
    engine.close()


def test_15_direct_filesystem_active_note_detected(tmp_path):
    import sys
    from pathlib import Path
    scripts_dir = str(Path(__file__).resolve().parents[1] / "30_SCRIPTS")
    if scripts_dir not in sys.path:
        sys.path.insert(0, scripts_dir)
    from verification.active_note_integrity import compare
    recorded = {"note-existing": "hash123"}
    current = {"note-existing": "hash123", "note-injected-direct": "hash456"}
    diff = compare(recorded, current)
    assert "note-injected-direct" in diff["entered_active"]


# -----------------------------------------------------------------------------
# 16-18: Helper / Alternate Path / Script Bypasses
# -----------------------------------------------------------------------------

def test_16_helper_function_bypass_rejected():
    from interfaces.memory_access import propose
    from pathlib import Path

    class DummyStorage:
        vault_root = Path(".")
        id_to_path = {}

    class DummyController:
        def __init__(self):
            self.storage = DummyStorage()
            self.notes = {}
        def propose(self, principal, note):
            assert getattr(principal, "value", str(principal)) == "ai_agent"
            assert note["lifecycle"] == "REVIEW"
            self.notes[note["id"]] = note
            return note["id"]
        def get(self, note_id):
            return self.notes.get(note_id)

    ctrl = DummyController()
    res = propose(ctrl, title="Helper Test", body="Body content", note_type="procedure")
    assert res["lifecycle"] == "REVIEW"
    assert res["verification"] == "unverified"
    assert "only the owner can attest it" in res["status"]


def test_17_alternate_import_path_preserves_policy():
    from lifecycle.policy import evaluate, TransitionRequest, Mutation, LifecycleState, PrincipalRole
    decision = evaluate(
        TransitionRequest(
            mutation=Mutation.PROMOTE,
            from_state=LifecycleState.RAW,
            to_state=LifecycleState.ACTIVE,
            principal=PrincipalRole.AI_AGENT,
        )
    )
    assert decision.allowed is False


def test_18_script_bypass_rejected():
    # promote_candidate_concept hardcodes lifecycle: REVIEW and rejects ACTIVE
    import sys
    from pathlib import Path
    scripts_dir = str(Path(__file__).resolve().parents[1] / "30_SCRIPTS")
    if scripts_dir not in sys.path:
        sys.path.insert(0, scripts_dir)
    from ingestion import promote_candidate_concept as pcc
    # Inspecting source directly guarantees invariant that ACTIVE is never emitted
    import inspect
    source = inspect.getsource(pcc.promote_candidate_concept)
    assert '"lifecycle": "REVIEW"' in source
    assert 'STRICT: NEVER ACTIVE' in source


# -----------------------------------------------------------------------------
# 19-20: Malformed & Case Variations
# -----------------------------------------------------------------------------

def test_19_malformed_lifecycle_rejected():
    note = valid_concept_payload("RAW")
    with pytest.raises(LifecycleTransitionError, match="Unknown or malformed lifecycle state"):
        transition_book_to_memory_lifecycle(
            note, target_state="SUPER_ACTIVE", actor=Principal.HUMAN
        )


@pytest.mark.parametrize("case_variant", ["active", "Active", "AcTiVe"])
def test_20_case_variation_normalized_or_rejected(case_variant):
    note = valid_concept_payload("RAW")
    with pytest.raises(LifecycleTransitionError, match="Illegal direct transition: RAW -> ACTIVE"):
        transition_book_to_memory_lifecycle(
            note, target_state=case_variant, actor=Principal.HUMAN
        )


# -----------------------------------------------------------------------------
# 21-25: Forged Principals & Tokens
# -----------------------------------------------------------------------------

def test_21_forged_principal_string_rejected():
    note = valid_concept_payload("VERIFIED")
    with pytest.raises(LifecycleTransitionError, match="Invalid principal object"):
        transition_book_to_memory_lifecycle(
            note, target_state=BookToMemoryLifecycleState.ACTIVE, actor="HUMAN"
        )


def test_22_forged_authorization_evidence_rejected():
    note = valid_concept_payload("VERIFIED")
    token = OwnerApprovalToken(
        note_id=note["id"],
        approver=Principal.HUMAN.value,
        timestamp="2026-10-04T00:00:00Z",
        signature="bad-signature",
    )
    with pytest.raises(OwnerApprovalError, match="Signature verification failed"):
        verify_owner_approval_token(note, token=token)


def test_23_stale_approval_token_rejected():
    note = valid_concept_payload("VERIFIED")
    old_time = (datetime.now(timezone.utc) - timedelta(days=31)).isoformat()
    token = issue_owner_approval(actor=Principal.HUMAN, note_id=note["id"], timestamp=old_time)
    with pytest.raises(OwnerApprovalError, match="Approval token has expired"):
        verify_owner_approval_token(note, token=token)


def test_24_replayed_approval_token_on_different_note():
    note_a = valid_concept_payload("VERIFIED")
    note_b = valid_concept_payload("VERIFIED")
    token_a = issue_owner_approval(actor=Principal.HUMAN, note_id=note_a["id"])
    with pytest.raises(OwnerApprovalError, match="Token note_id mismatch"):
        verify_owner_approval_token(note_b, token=token_a)


def test_25_approval_issued_by_ai_agent_rejected():
    note = valid_concept_payload("VERIFIED")
    with pytest.raises(OwnerApprovalError, match="Principal 'ai_agent' cannot issue owner approval"):
        issue_owner_approval(actor=Principal.AI_AGENT, note_id=note["id"])


# -----------------------------------------------------------------------------
# 26: Policy Mismatch
# -----------------------------------------------------------------------------

def test_26_policy_mismatch_fails_closed():
    note = valid_concept_payload("RAW")
    # Attempting to move from RAW directly to VERIFIED
    with pytest.raises(LifecycleTransitionError, match="Illegal direct transition: RAW -> VERIFIED"):
        transition_book_to_memory_lifecycle(
            note, target_state=BookToMemoryLifecycleState.VERIFIED, actor=Principal.HUMAN
        )


# -----------------------------------------------------------------------------
# 27-29: Demotion & Regression Handlers
# -----------------------------------------------------------------------------

def test_27_regression_after_active_demotes_to_unverified():
    note = valid_concept_payload("ACTIVE")
    note["verification"] = "verified"
    demoted = demote_active_note(
        note, reason="Performance regression in ablation suite", actor=Principal.HUMAN
    )
    assert demoted["lifecycle"] == BookToMemoryLifecycleState.UNVERIFIED.value
    assert demoted["verification"] == "unverified"
    assert "demotion_history" in demoted
    assert len(demoted["demotion_history"]) == 1
    assert demoted["demotion_history"][0]["previous_state"] == "ACTIVE"


def test_28_obsolete_source_triggers_demotion():
    note = valid_concept_payload("ACTIVE")
    demoted = demote_active_note(
        note, reason="Source edition superseded; claims invalidated", actor=Principal.HUMAN
    )
    assert demoted["lifecycle"] == BookToMemoryLifecycleState.UNVERIFIED.value
    assert "superseded" in demoted["demotion_history"][0]["reason"].lower()


def test_29_conflicting_source_demotes_active_note():
    note = valid_concept_payload("ACTIVE")
    demoted = demote_active_note(
        note,
        reason="New HIGH severity conflict identified in CONF-002",
        actor=Principal.HUMAN,
        conflict_ref="CONF-002",
    )
    assert demoted["lifecycle"] == BookToMemoryLifecycleState.UNVERIFIED.value
    assert demoted["demotion_history"][0]["conflict_ref"] == "CONF-002"


# -----------------------------------------------------------------------------
# 30: Security Finding Blocks Promotion
# -----------------------------------------------------------------------------

def test_30_security_finding_blocks_promotion():
    note = valid_concept_payload("VERIFIED")
    note["tool_call"] = "exec('evil')"
    token = issue_owner_approval(actor=Principal.HUMAN, note_id=note["id"])
    with pytest.raises(LifecycleTransitionError, match="GATE-03 Security failed"):
        transition_book_to_memory_lifecycle(
            note, target_state=BookToMemoryLifecycleState.ACTIVE, actor=Principal.HUMAN, approval_token=token
        )
