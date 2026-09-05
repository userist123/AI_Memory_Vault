import os
import shutil
import tempfile
import uuid

import pytest

from memory_controller.authorizer import Principal
from memory_controller.controller import Lifecycle, MemoryController, StorageEngine
from memory_controller.storage.sqlite_engine import SQLiteStorageEngine


def _note(lifecycle: str, note_type: str = "knowledge"):
    note_id = str(uuid.uuid4())
    return note_id, {
        "id": note_id,
        "type": note_type,
        "lifecycle": lifecycle,
        "category": "security-test",
        "tags": [],
        "created": "2026-09-05",
        "updated": "2026-09-05",
        "provenance": {"source_type": "inference", "source_ref": "query-boundary-test"},
        "confidence": "high",
        "verification": "unverified",
        "relations": [],
        "content": "query boundary test",
    }


def test_query_never_exposes_raw_notes_from_in_memory_storage():
    storage = StorageEngine()
    raw_id, raw_note = _note(Lifecycle.RAW.value)
    review_id, review_note = _note(Lifecycle.REVIEW.value)
    storage.set(raw_id, raw_note)
    storage.set(review_id, review_note)

    controller = MemoryController(storage)

    all_visible = controller.query(Principal.HUMAN)
    assert {note["id"] for note in all_visible} == {review_id}

    explicitly_raw = controller.query(Principal.HUMAN, lifecycles=[Lifecycle.RAW])
    assert explicitly_raw == []


def test_query_normalizes_lifecycle_enums_before_storage_filtering():
    storage = StorageEngine()
    active_id, active_note = _note(Lifecycle.ACTIVE.value)
    review_id, review_note = _note(Lifecycle.REVIEW.value)
    storage.set(active_id, active_note)
    storage.set(review_id, review_note)

    controller = MemoryController(storage)

    result = controller.query(Principal.AI_AGENT, lifecycles=[Lifecycle.ACTIVE])
    assert [note["id"] for note in result] == [active_id]


@pytest.mark.parametrize("principal", [Principal.HUMAN, Principal.AI_AGENT, Principal.ADMIN])
def test_query_raw_boundary_applies_to_all_read_principals(principal):
    storage = StorageEngine()
    raw_id, raw_note = _note(Lifecycle.RAW.value)
    active_id, active_note = _note(Lifecycle.ACTIVE.value)
    storage.set(raw_id, raw_note)
    storage.set(active_id, active_note)

    controller = MemoryController(storage)

    result = controller.query(principal)
    assert {note["id"] for note in result} == {active_id}
    assert raw_id not in {note["id"] for note in result}


def test_query_raw_boundary_holds_for_sqlite_storage():
    fd, path = tempfile.mkstemp(suffix=".sqlite3")
    os.close(fd)
    try:
        storage = SQLiteStorageEngine(path, wal_mode=False)
        raw_id, raw_note = _note(Lifecycle.RAW.value)
        active_id, active_note = _note(Lifecycle.ACTIVE.value)
        storage.set(raw_id, raw_note)
        storage.set(active_id, active_note)

        controller = MemoryController(storage)

        result = controller.query(Principal.HUMAN)
        assert {note["id"] for note in result} == {active_id}
        assert controller.query(Principal.HUMAN, lifecycles=[Lifecycle.RAW]) == []
    finally:
        # Windows keeps the sqlite3 file handle open until the connection is
        # explicitly closed; without this, os.remove() below intermittently
        # raises PermissionError (WinError 32) instead of just FileNotFoundError.
        try:
            storage.close()
        except Exception:
            pass
        for suffix in ("", "-wal", "-shm"):
            try:
                os.remove(path + suffix)
            except (FileNotFoundError, PermissionError):
                pass
