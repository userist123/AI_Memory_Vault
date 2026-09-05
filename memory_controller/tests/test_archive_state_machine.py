"""archive() lifecycle state-machine tests (runtime security front, owner:
claude-code).

Previously archive() had NO lifecycle restriction at all: RAW -> ARCHIVED,
CLASSIFIED -> ARCHIVED, an already-SUPERSEDED note -> ARCHIVED again, etc.,
were all silently accepted, with no evidence requirement enforced beyond an
unused `reason: str` parameter that was never validated non-empty.

Decision (see 00_GOVERNANCE/coordination/claude-code/ ADR response): only
ACTIVE and REVIEW notes may be archived; archiving a human-verified ACTIVE
note additionally requires ADMIN (not plain HUMAN); `reason` must be
non-empty; every transition is fully audited with previous/new lifecycle.
"""
from __future__ import annotations

import uuid

import pytest

from memory_controller.controller import MemoryController, StorageEngine, Lifecycle
from memory_controller.authorizer import Principal


def _note(note_id, lifecycle, verification="unverified", **overrides):
    base = {
        "id": note_id,
        "type": "knowledge",
        "lifecycle": lifecycle,
        "category": "test",
        "tags": [],
        "created": "2026-01-01",
        "updated": "2026-01-01",
        "provenance": {"source_type": "user", "source_ref": "test"},
        "confidence": "high",
        "verification": verification,
        "relations": [],
        "content": "body",
    }
    base.update(overrides)
    return base


@pytest.fixture
def controller():
    return MemoryController(StorageEngine())


@pytest.mark.parametrize("lifecycle", ["RAW", "CLASSIFIED", "NORMALIZED"])
def test_archive_rejects_pre_review_lifecycles(controller, lifecycle):
    nid = str(uuid.uuid4())
    controller.storage.set(nid, _note(nid, lifecycle))
    with pytest.raises(ValueError, match="Cannot archive a note in lifecycle"):
        controller.archive(Principal.HUMAN, nid, "attempted premature archive")
    assert controller.storage.get(nid)["lifecycle"] == lifecycle


def test_archive_rejects_already_superseded(controller):
    nid = str(uuid.uuid4())
    controller.storage.set(nid, _note(nid, "SUPERSEDED"))
    with pytest.raises(ValueError, match="Cannot archive a note in lifecycle"):
        controller.archive(Principal.HUMAN, nid, "reason")


def test_archive_rejects_already_archived_reidempotent_reuse(controller):
    nid = str(uuid.uuid4())
    controller.storage.set(nid, _note(nid, "ARCHIVED"))
    with pytest.raises(ValueError, match="Cannot archive a note in lifecycle"):
        controller.archive(Principal.HUMAN, nid, "re-archive with a different reason")


def test_archive_allows_active_unverified_by_human(controller):
    nid = str(uuid.uuid4())
    controller.storage.set(nid, _note(nid, "ACTIVE", verification="unverified"))
    controller.archive(Principal.HUMAN, nid, "no longer relevant")
    assert controller.storage.get(nid)["lifecycle"] == Lifecycle.ARCHIVED


def test_archive_allows_review(controller):
    nid = str(uuid.uuid4())
    controller.storage.set(nid, _note(nid, "REVIEW"))
    controller.archive(Principal.HUMAN, nid, "rejected during review")
    assert controller.storage.get(nid)["lifecycle"] == Lifecycle.ARCHIVED


def test_archive_of_verified_active_note_requires_admin(controller):
    nid = str(uuid.uuid4())
    controller.storage.set(nid, _note(nid, "ACTIVE", verification="verified"))
    with pytest.raises(PermissionError, match="requires ADMIN authorization"):
        controller.archive(Principal.HUMAN, nid, "trying to archive verified note as human")
    assert controller.storage.get(nid)["lifecycle"] == Lifecycle.ACTIVE


def test_archive_of_verified_active_note_allowed_for_admin(controller):
    nid = str(uuid.uuid4())
    controller.storage.set(nid, _note(nid, "ACTIVE", verification="verified"))
    controller.archive(Principal.ADMIN, nid, "admin-authorized archive of verified note")
    assert controller.storage.get(nid)["lifecycle"] == Lifecycle.ARCHIVED


def test_archive_requires_non_empty_reason(controller):
    nid = str(uuid.uuid4())
    controller.storage.set(nid, _note(nid, "ACTIVE"))
    with pytest.raises(ValueError, match="non-empty reason"):
        controller.archive(Principal.HUMAN, nid, "")
    with pytest.raises(ValueError, match="non-empty reason"):
        controller.archive(Principal.HUMAN, nid, "   ")
    assert controller.storage.get(nid)["lifecycle"] == "ACTIVE"


def test_archive_ai_agent_denied_by_authorization_regardless_of_lifecycle(controller):
    nid = str(uuid.uuid4())
    controller.storage.set(nid, _note(nid, "ACTIVE"))
    with pytest.raises(PermissionError):
        controller.archive(Principal.AI_AGENT, nid, "ai attempting archive")
    assert controller.storage.get(nid)["lifecycle"] == "ACTIVE"


def test_archive_audit_records_previous_and_new_lifecycle(controller, monkeypatch):
    captured = {}
    import memory_controller.controller as controller_module

    def fake_audit_event(operation, principal, target_id, success=True, details=None):
        if operation == "archive" and success:
            captured.update(details or {})

    monkeypatch.setattr(controller_module, "audit_event", fake_audit_event)
    nid = str(uuid.uuid4())
    controller.storage.set(nid, _note(nid, "ACTIVE"))
    controller.archive(Principal.HUMAN, nid, "test evidence trail")
    assert captured.get("previous_lifecycle") == "ACTIVE"
    assert captured.get("new_lifecycle") == "ARCHIVED"
    assert captured.get("reason") == "test evidence trail"


def test_archive_nonexistent_note_raises(controller):
    with pytest.raises(ValueError, match="Note not found"):
        controller.archive(Principal.HUMAN, "does-not-exist", "reason")
