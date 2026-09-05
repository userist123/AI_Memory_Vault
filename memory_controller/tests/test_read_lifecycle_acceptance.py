import uuid

import pytest

from memory_controller.authorizer import Principal
from memory_controller.controller import Lifecycle, MemoryController, StorageEngine


def _note(lifecycle="REVIEW", verification="unverified"):
    return {
        "id": str(uuid.uuid4()),
        "type": "knowledge",
        "lifecycle": lifecycle,
        "category": "acceptance",
        "tags": [],
        "created": "2026-09-05",
        "updated": "2026-09-05",
        "provenance": {"source_type": "user", "source_ref": "read-acceptance"},
        "confidence": "medium",
        "verification": verification,
        "relations": [],
        "content": "acceptance test content",
    }


def _controller_with(note):
    storage = StorageEngine()
    storage.set(note["id"], note)
    return MemoryController(storage), storage


def test_public_read_allows_only_active_notes():
    active = _note(Lifecycle.ACTIVE.value, "verified")
    controller, _ = _controller_with(active)

    result = controller.read(Principal.HUMAN, active["id"])

    assert result["results"]
    assert result["results"][0]["id"] == active["id"]


@pytest.mark.parametrize("lifecycle", [
    Lifecycle.RAW.value,
    Lifecycle.CLASSIFIED.value,
    Lifecycle.NORMALIZED.value,
    Lifecycle.REVIEW.value,
    Lifecycle.VERIFIED.value,
    Lifecycle.RECONSOLIDATING.value,
    Lifecycle.SUPERSEDED.value,
    Lifecycle.ARCHIVED.value,
])
def test_public_read_rejects_every_non_active_lifecycle(lifecycle):
    note = _note(lifecycle, "verified" if lifecycle == Lifecycle.VERIFIED.value else "unverified")
    controller, _ = _controller_with(note)

    with pytest.raises(ValueError, match="Only ACTIVE notes are readable via public API"):
        controller.read(Principal.HUMAN, note["id"])


def test_cognitive_read_marks_review_notes_as_unverified():
    review = _note(Lifecycle.REVIEW.value, "unverified")
    controller, _ = _controller_with(review)

    result = controller.cognitive_read(Principal.AI_AGENT, review["id"])

    assert result["results"][0]["id"] == review["id"]
    assert result["results"][0]["_cognitive_unverified"] is True
    assert result["results"][0]["verification"] == "unverified"


@pytest.mark.parametrize("lifecycle", [
    Lifecycle.RAW.value,
    Lifecycle.CLASSIFIED.value,
    Lifecycle.NORMALIZED.value,
    Lifecycle.RECONSOLIDATING.value,
    Lifecycle.SUPERSEDED.value,
    Lifecycle.ARCHIVED.value,
])
def test_cognitive_read_rejects_lifecycles_outside_active_or_review(lifecycle):
    note = _note(lifecycle)
    controller, _ = _controller_with(note)

    with pytest.raises(ValueError, match="not eligible for cognitive retrieval"):
        controller.cognitive_read(Principal.AI_AGENT, note["id"])


def test_cognitive_read_allows_active_without_unverified_marker():
    active = _note(Lifecycle.ACTIVE.value, "verified")
    controller, _ = _controller_with(active)

    result = controller.cognitive_read(Principal.AI_AGENT, active["id"])

    assert result["results"][0]["lifecycle"] == Lifecycle.ACTIVE.value
    assert "_cognitive_unverified" not in result["results"][0]


def test_storage_query_excludes_raw_notes_by_default():
    storage = StorageEngine()
    raw = _note(Lifecycle.RAW.value)
    review = _note(Lifecycle.REVIEW.value)
    active = _note(Lifecycle.ACTIVE.value, "verified")
    for note in (raw, review, active):
        storage.set(note["id"], note)

    results = storage.query(intent="", lifecycle=None)

    result_ids = {note["id"] for note in results}
    assert raw["id"] not in result_ids
    assert {review["id"], active["id"]} <= result_ids


def test_storage_query_honors_explicit_lifecycle_ceiling():
    storage = StorageEngine()
    review = _note(Lifecycle.REVIEW.value)
    active = _note(Lifecycle.ACTIVE.value, "verified")
    archived = _note(Lifecycle.ARCHIVED.value)
    for note in (review, active, archived):
        storage.set(note["id"], note)

    results = storage.query(intent="", lifecycle=[Lifecycle.REVIEW.value, Lifecycle.ACTIVE.value])

    result_ids = {note["id"] for note in results}
    assert result_ids == {review["id"], active["id"]}
