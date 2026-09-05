from memory_controller.storage.sqlite_engine import SQLiteStorageEngine


def _note(note_id: str, lifecycle: str):
    return {
        "id": note_id,
        "type": "knowledge",
        "lifecycle": lifecycle,
        "category": "security-test",
        "tags": [],
        "created": "2026-09-05",
        "updated": "2026-09-05",
        "provenance": {"source_type": "execution", "source_ref": "test"},
        "confidence": "high",
        "verification": "verified",
        "relations": [],
        "content": "test",
    }


def test_sqlite_accepts_reconsolidating_lifecycle():
    storage = SQLiteStorageEngine(":memory:")
    note_id = "recon-1"
    storage.set(note_id, _note(note_id, "RECONSOLIDATING"))

    stored = storage.get(note_id)
    assert stored is not None
    assert stored["lifecycle"] == "RECONSOLIDATING"


def test_sqlite_query_returns_reconsolidating_but_not_raw():
    storage = SQLiteStorageEngine(":memory:")
    recon_id = "recon-2"
    raw_id = "raw-2"
    storage.set(recon_id, _note(recon_id, "RECONSOLIDATING"))
    storage.set(raw_id, _note(raw_id, "RAW"))

    results = storage.query(intent="", lifecycle=["RECONSOLIDATING", "RAW"])
    ids = {note["id"] for note in results}
    assert ids == {recon_id}
