import pytest

from cognitive_core.consolidation import Consolidator
from memory_controller.authorizer import DefaultAuthorizer, Principal
from memory_controller.controller import Lifecycle, MemoryController, StorageEngine


class _RouterStub:
    def execute(self, *args, **kwargs):
        raise AssertionError("reconsolidation test must not route through lesson consolidation")


def _controller():
    return MemoryController(StorageEngine(), DefaultAuthorizer())


def _active_note(controller, note_id="note-1"):
    note = {
        "id": note_id,
        "type": "knowledge",
        "lifecycle": Lifecycle.ACTIVE.value,
        "category": "test",
        "tags": [],
        "created": "2026-09-05",
        "updated": "2026-09-05",
        "provenance": {"source_type": "ai", "source_ref": "test"},
        "confidence": "high",
        "verification": "verified",
        "content": "original canonical content",
        "relations": [],
    }
    controller.storage.set(note_id, note)
    return note


def test_reconsolidation_requires_explicit_principal():
    controller = _controller()
    consolidator = Consolidator(controller, _RouterStub())
    _active_note(controller)

    with pytest.raises(TypeError):
        consolidator.challenge("note-1", {"source": "conflict"})


def test_ai_can_challenge_but_cannot_resolve_to_active():
    controller = _controller()
    consolidator = Consolidator(controller, _RouterStub())
    _active_note(controller)

    challenged = consolidator.challenge(
        "note-1", {"source": "conflict", "claim": "contradiction"}, Principal.AI_AGENT
    )

    assert challenged["lifecycle"] == Lifecycle.RECONSOLIDATING.value
    assert challenged["previous_version"]["lifecycle"] == Lifecycle.ACTIVE.value

    with pytest.raises(PermissionError):
        consolidator.resolve_challenge(
            "note-1",
            {"content": "attacker-controlled replacement"},
            Principal.AI_AGENT,
        )


def test_human_resolution_reenters_review_and_resets_verification():
    controller = _controller()
    consolidator = Consolidator(controller, _RouterStub())
    _active_note(controller)

    consolidator.challenge("note-1", {"source": "conflict"}, Principal.AI_AGENT)
    resolved = consolidator.resolve_challenge(
        "note-1", {"content": "reviewed replacement", "relations": []}, Principal.HUMAN
    )

    assert resolved["lifecycle"] == Lifecycle.REVIEW.value
    assert resolved["verification"] == "unverified"
    assert resolved["content"] == "reviewed replacement"
    assert "verification_source" not in resolved
    assert "last_verified" not in resolved


def test_admin_can_resolve_unresolved_challenge_but_it_still_cannot_be_active():
    controller = _controller()
    consolidator = Consolidator(controller, _RouterStub())
    _active_note(controller)

    consolidator.challenge("note-1", {"source": "conflict"}, Principal.AI_AGENT)
    resolved = consolidator.resolve_challenge("note-1", None, Principal.ADMIN)

    assert resolved["lifecycle"] == Lifecycle.REVIEW.value
    assert resolved["verification"] == "unverified"
