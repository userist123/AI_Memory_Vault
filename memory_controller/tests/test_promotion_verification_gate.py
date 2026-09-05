import uuid

import pytest

from memory_controller.authorizer import Principal
from memory_controller.controller import Lifecycle, MemoryController
from memory_controller.storage.sqlite_engine import SQLiteStorageEngine


def make_note(note_id: str, verification: str = "unverified"):
    return {
        "id": note_id,
        "type": "knowledge",
        "lifecycle": Lifecycle.REVIEW.value,
        "category": "security-adversarial",
        "tags": ["promotion-gate"],
        "created": "2026-08-14",
        "updated": "2026-08-14",
        "provenance": {"source_type": "inference", "source_ref": "promotion-gate-test"},
        "confidence": "high",
        "verification": verification,
        "relations": [],
        "content": "promotion gate regression",
    }


def test_unverified_review_note_cannot_be_promoted(temp_path):
    storage = SQLiteStorageEngine(temp_path)
    controller = MemoryController(storage)
    note_id = str(uuid.uuid4())
    controller.propose(Principal.AI_AGENT, make_note(note_id))

    with pytest.raises(ValueError, match="Only VERIFIED notes can be promoted"):
        controller.promote(Principal.HUMAN, note_id)

    assert storage.get(note_id)["lifecycle"] == Lifecycle.REVIEW.value
    assert storage.get(note_id)["verification"] == "unverified"


def test_human_verified_review_note_can_be_promoted(temp_path):
    storage = SQLiteStorageEngine(temp_path)
    controller = MemoryController(storage)
    note_id = str(uuid.uuid4())
    controller.propose(Principal.HUMAN, make_note(note_id))
    controller.attest(Principal.HUMAN, note_id, "validated by human", "promotion-gate-test")

    assert storage.get(note_id)["verification"] == "verified"
    controller.promote(Principal.HUMAN, note_id)

    assert storage.get(note_id)["lifecycle"] == Lifecycle.ACTIVE.value


@pytest.fixture
def temp_path(tmp_path):
    return str(tmp_path / "promotion.sqlite3")
