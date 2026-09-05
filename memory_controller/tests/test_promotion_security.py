import uuid

import pytest

from memory_controller.authorizer import Principal
from memory_controller.controller import MemoryController
from memory_controller.storage.sqlite_engine import SQLiteStorageEngine


def _note(note_id: str):
    return {
        "id": note_id,
        "type": "knowledge",
        "lifecycle": "REVIEW",
        "category": "security-regression",
        "tags": ["p0", "promotion"],
        "created": "2026-09-05",
        "updated": "2026-09-05",
        "provenance": {"source_type": "inference", "source_ref": "test"},
        "confidence": "high",
        "verification": "unverified",
        "relations": [],
        "content": "promotion security regression",
    }


def test_promote_requires_human_verification(tmp_path):
    """A REVIEW note cannot become ACTIVE until independently attested."""
    storage = SQLiteStorageEngine(str(tmp_path / "memory.sqlite3"), wal_mode=True)
    controller = MemoryController(storage)
    note_id = str(uuid.uuid4())
    controller.propose(Principal.AI_AGENT, _note(note_id))

    with pytest.raises(ValueError, match="Only VERIFIED notes can be promoted to ACTIVE"):
        controller.promote(Principal.HUMAN, note_id)

    stored = storage.get(note_id)
    assert stored["lifecycle"] == "REVIEW"
    assert stored["verification"] == "unverified"
    assert "verification_source" not in stored


def test_promote_after_attestation_still_succeeds(tmp_path):
    """The verified path remains valid after the new promotion gate."""
    storage = SQLiteStorageEngine(str(tmp_path / "memory.sqlite3"), wal_mode=True)
    controller = MemoryController(storage)
    note_id = str(uuid.uuid4())
    controller.propose(Principal.AI_AGENT, _note(note_id))
    controller.attest(Principal.HUMAN, note_id, "Reviewed against evidence", "evidence-ref")
    controller.promote(Principal.HUMAN, note_id)

    stored = storage.get(note_id)
    assert stored["verification"] == "verified"
    assert stored["verification_source"] == "human"
    assert stored["lifecycle"] == "ACTIVE"
