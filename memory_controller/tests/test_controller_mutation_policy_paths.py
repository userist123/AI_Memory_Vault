import uuid

import pytest

from memory_controller.authorizer import Principal
from memory_controller.controller import Lifecycle, MemoryController, StorageEngine


def _note(lifecycle, verification="unverified"):
    return {
        "id": str(uuid.uuid4()),
        "type": "knowledge",
        "lifecycle": lifecycle,
        "category": "mutation-policy",
        "tags": [],
        "created": "2026-09-05",
        "updated": "2026-09-05",
        "provenance": {"source_type": "user", "source_ref": "mutation-policy-test"},
        "confidence": "medium",
        "verification": verification,
        "relations": [],
        "content": "mutation policy test content",
    }


def _controller_with(note):
    storage = StorageEngine()
    storage.set(note["id"], note)
    return MemoryController(storage), storage


def test_review_public_mutation_executes_canonical_review_transition():
    note = _note(Lifecycle.NORMALIZED.value)
    controller, storage = _controller_with(note)

    controller.review(Principal.HUMAN, note["id"], "approve")

    assert storage.get(note["id"])["lifecycle"] == Lifecycle.REVIEW.value


def test_promote_public_mutation_requires_verified_review_and_reaches_active():
    note = _note(Lifecycle.REVIEW.value, "verified")
    controller, storage = _controller_with(note)

    controller.promote(Principal.HUMAN, note["id"])

    assert storage.get(note["id"])["lifecycle"] == Lifecycle.ACTIVE.value


def test_promote_public_mutation_rejects_unverified_review_without_write():
    note = _note(Lifecycle.REVIEW.value, "unverified")
    controller, storage = _controller_with(note)

    with pytest.raises(ValueError, match="VERIFIED"):
        controller.promote(Principal.HUMAN, note["id"])

    assert storage.get(note["id"])["lifecycle"] == Lifecycle.REVIEW.value


@pytest.mark.parametrize("lifecycle", [Lifecycle.REVIEW.value, Lifecycle.ACTIVE.value])
def test_archive_public_mutation_uses_canonical_archive_targets(lifecycle):
    verification = "verified" if lifecycle == Lifecycle.ACTIVE.value else "unverified"
    note = _note(lifecycle, verification)
    controller, storage = _controller_with(note)

    controller.archive(Principal.HUMAN, note["id"], "policy test")

    assert storage.get(note["id"])["lifecycle"] == Lifecycle.ARCHIVED.value


def test_archive_public_mutation_rejects_verified_lifecycle_without_write():
    note = _note(Lifecycle.VERIFIED.value, "verified")
    controller, storage = _controller_with(note)

    with pytest.raises(ValueError, match="Only REVIEW or ACTIVE"):
        controller.archive(Principal.HUMAN, note["id"], "policy test")

    assert storage.get(note["id"])["lifecycle"] == Lifecycle.VERIFIED.value
