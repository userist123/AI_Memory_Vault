"""Regression tests for proposal lifecycle trust boundary."""

import uuid

import pytest

from memory_controller.controller import Lifecycle, MemoryController, StorageEngine
from memory_controller.authorizer import Principal


def _note_data(lifecycle: str = Lifecycle.REVIEW.value) -> dict:
    return {
        "id": str(uuid.uuid4()),
        "type": "knowledge",
        "category": "security",
        "tags": ["test"],
        "lifecycle": lifecycle,
        "created": "2026-09-05",
        "updated": "2026-09-05",
        "provenance": {"source_type": "user", "source_ref": "test"},
        "confidence": "high",
        "verification": "unverified",
        "relations": [],
        "content": "proposal boundary regression",
    }


@pytest.mark.parametrize("principal", [Principal.HUMAN, Principal.AI_AGENT, Principal.ADMIN])
@pytest.mark.parametrize(
    "lifecycle",
    [
        Lifecycle.VERIFIED.value,
        Lifecycle.ACTIVE.value,
        Lifecycle.RECONSOLIDATING.value,
        Lifecycle.SUPERSEDED.value,
        Lifecycle.ARCHIVED.value,
    ],
)
def test_propose_rejects_privileged_creation_for_every_principal(principal, lifecycle):
    controller = MemoryController(StorageEngine())

    with pytest.raises(ValueError, match="cannot set lifecycle"):
        controller.propose(principal, _note_data(lifecycle))


@pytest.mark.parametrize("principal", [Principal.HUMAN, Principal.AI_AGENT, Principal.ADMIN])
def test_propose_accepts_review_for_every_principal(principal):
    controller = MemoryController(StorageEngine())
    note = _note_data(Lifecycle.REVIEW.value)

    note_id = controller.propose(principal, note)

    stored = controller.storage.get(note_id)
    assert stored["lifecycle"] == Lifecycle.REVIEW.value
    assert stored["verification"] == "unverified"


@pytest.mark.parametrize("reason", [None, "", "   "])
def test_archive_requires_non_empty_reason(reason):
    controller = MemoryController(StorageEngine())
    note = _note_data(Lifecycle.REVIEW.value)
    note_id = controller.propose(Principal.HUMAN, note)

    with pytest.raises(ValueError, match="non-empty reason"):
        controller.archive(Principal.HUMAN, note_id, reason)


def test_archive_allows_review_with_reason():
    controller = MemoryController(StorageEngine())
    note = _note_data(Lifecycle.REVIEW.value)
    note_id = controller.propose(Principal.HUMAN, note)

    controller.archive(Principal.HUMAN, note_id, "retired test candidate")

    stored = controller.storage.get(note_id)
    assert stored["lifecycle"] == Lifecycle.ARCHIVED.value
    assert stored["archive_reason"] == "retired test candidate"
