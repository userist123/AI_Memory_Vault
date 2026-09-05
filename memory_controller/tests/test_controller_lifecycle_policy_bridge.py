import uuid

import pytest

from memory_controller.controller import Lifecycle, MemoryController, StorageEngine


def _note(lifecycle="REVIEW", verification="unverified"):
    return {
        "id": str(uuid.uuid4()),
        "type": "knowledge",
        "lifecycle": lifecycle,
        "category": "test",
        "tags": [],
        "created": "2026-09-05",
        "updated": "2026-09-05",
        "provenance": {"source_type": "user", "source_ref": "controller-test"},
        "confidence": "medium",
        "verification": verification,
        "relations": [],
    }


def test_validate_note_routes_review_to_active_through_policy():
    storage = StorageEngine()
    controller = MemoryController(storage)
    existing = _note("REVIEW", "unverified")
    storage.set(existing["id"], existing)

    candidate = dict(existing)
    candidate["lifecycle"] = Lifecycle.ACTIVE.value

    with pytest.raises(ValueError, match="Invalid lifecycle transition"):
        controller._validate_note(candidate)


def test_validate_note_allows_verified_review_to_active():
    storage = StorageEngine()
    controller = MemoryController(storage)
    existing = _note("REVIEW", "verified")
    storage.set(existing["id"], existing)

    candidate = dict(existing)
    candidate["lifecycle"] = Lifecycle.ACTIVE.value

    controller._validate_note(candidate)


def test_validate_note_allows_active_to_archived_through_policy():
    storage = StorageEngine()
    controller = MemoryController(storage)
    existing = _note("ACTIVE", "verified")
    storage.set(existing["id"], existing)

    candidate = dict(existing)
    candidate["lifecycle"] = Lifecycle.ARCHIVED.value

    controller._validate_note(candidate)
