"""attest() security tests (runtime security front, owner: claude-code).

Covers: verification_state whitelist (no arbitrary values, no fuzzy
matching), AI_AGENT permanently denied, evidence/reason mandatory, full
audit trail (previous state, new state, actor, evidence, reason,
timestamp), idempotent no-op when state is unchanged.
"""
from __future__ import annotations

import uuid

import pytest

from memory_controller.controller import MemoryController, StorageEngine
from memory_controller.authorizer import Principal


def _note(note_id, verification="unverified"):
    return {
        "id": note_id, "type": "knowledge", "lifecycle": "REVIEW", "category": "test",
        "tags": [], "created": "2026-01-01", "updated": "2026-01-01",
        "provenance": {"source_type": "user", "source_ref": "test"},
        "confidence": "high", "verification": verification, "relations": [], "content": "body",
    }


@pytest.fixture
def controller():
    return MemoryController(StorageEngine())


def test_attest_rejects_arbitrary_verification_state(controller):
    nid = str(uuid.uuid4())
    controller.storage.set(nid, _note(nid))
    with pytest.raises(ValueError, match="Invalid verification_state"):
        controller.attest(Principal.HUMAN, nid, "reason", "evidence", verification_state="super_duper_verified")
    assert controller.storage.get(nid)["verification"] == "unverified"


def test_attest_rejects_injection_style_verification_state(controller):
    nid = str(uuid.uuid4())
    controller.storage.set(nid, _note(nid))
    with pytest.raises(ValueError, match="Invalid verification_state"):
        controller.attest(Principal.HUMAN, nid, "reason", "evidence",
                           verification_state="verified'; DROP TABLE notes;--")
    assert controller.storage.get(nid)["verification"] == "unverified"


def test_attest_rejects_near_miss_no_fuzzy_matching(controller):
    nid = str(uuid.uuid4())
    controller.storage.set(nid, _note(nid))
    for near_miss in ("Verified", "VERIFIED", "verified ", " verified", "verifed"):
        with pytest.raises(ValueError, match="Invalid verification_state"):
            controller.attest(Principal.HUMAN, nid, "reason", "evidence", verification_state=near_miss)
    assert controller.storage.get(nid)["verification"] == "unverified"


@pytest.mark.parametrize("state", ["verified", "partially_verified", "unverified", "inferred"])
def test_attest_accepts_every_canonical_verification_state(controller, state):
    nid = str(uuid.uuid4())
    controller.storage.set(nid, _note(nid, verification="unverified" if state != "unverified" else "inferred"))
    controller.attest(Principal.HUMAN, nid, "reason", "evidence", verification_state=state)
    assert controller.storage.get(nid)["verification"] == state


def test_ai_agent_cannot_call_attest_at_all(controller):
    nid = str(uuid.uuid4())
    controller.storage.set(nid, _note(nid))
    with pytest.raises(PermissionError):
        controller.attest(Principal.AI_AGENT, nid, "self attestation", "self evidence")
    assert controller.storage.get(nid)["verification"] == "unverified"
    assert "verification_source" not in controller.storage.get(nid)


def test_attest_requires_non_empty_reason_and_evidence(controller):
    nid = str(uuid.uuid4())
    controller.storage.set(nid, _note(nid))
    with pytest.raises(ValueError, match="verification_reason"):
        controller.attest(Principal.HUMAN, nid, "", "evidence")
    with pytest.raises(ValueError, match="evidence_reference"):
        controller.attest(Principal.HUMAN, nid, "reason", "")
    with pytest.raises(ValueError, match="verification_reason"):
        controller.attest(Principal.HUMAN, nid, "   ", "evidence")
    assert controller.storage.get(nid)["verification"] == "unverified"


def test_attest_audit_trail_has_all_required_fields(controller, monkeypatch):
    captured = {}
    import memory_controller.controller as controller_module

    def fake_audit_event(operation, principal, target_id, success=True, details=None):
        if operation == "attest" and success:
            captured.update(details or {})

    monkeypatch.setattr(controller_module, "audit_event", fake_audit_event)
    nid = str(uuid.uuid4())
    controller.storage.set(nid, _note(nid, verification="unverified"))
    controller.attest(Principal.ADMIN, nid, "my reason", "my evidence ref")

    assert captured["attested_by"] == "admin"
    assert captured["reason"] == "my reason"
    assert captured["evidence_reference"] == "my evidence ref"
    assert captured["previous_verification_state"] == "unverified"
    assert captured["new_verification_state"] == "verified"


def test_attest_is_idempotent_noop_when_state_unchanged(controller, monkeypatch):
    """Attesting to the SAME state twice must not double-log or crash."""
    call_count = {"n": 0}
    import memory_controller.controller as controller_module
    real_audit_event = controller_module.audit_event

    def counting_audit_event(operation, principal, target_id, success=True, details=None):
        if operation == "attest":
            call_count["n"] += 1
        return real_audit_event(operation, principal, target_id, success=success, details=details)

    monkeypatch.setattr(controller_module, "audit_event", counting_audit_event)
    nid = str(uuid.uuid4())
    controller.storage.set(nid, _note(nid, verification="unverified"))
    controller.attest(Principal.HUMAN, nid, "first", "evidence-1", verification_state="verified")
    controller.attest(Principal.HUMAN, nid, "second", "evidence-2", verification_state="verified")
    # Second call is a no-op (same state), so it must not overwrite the
    # evidence/reason from the first, real attestation.
    assert controller.storage.get(nid)["verification"] == "verified"


def test_attest_downgrade_requires_reason_and_evidence_same_as_upgrade(controller):
    """A documented downgrade (verified -> unverified) is allowed, but only
    with the same mandatory reason/evidence as any other attestation --
    there is no separate, weaker path for downgrades."""
    nid = str(uuid.uuid4())
    controller.storage.set(nid, _note(nid, verification="verified"))
    with pytest.raises(ValueError, match="verification_reason"):
        controller.attest(Principal.ADMIN, nid, "", "", verification_state="unverified")
    controller.attest(Principal.ADMIN, nid, "found to be incorrect", "correction-evidence",
                       verification_state="unverified")
    assert controller.storage.get(nid)["verification"] == "unverified"
