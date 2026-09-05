import sqlite3

from memory_controller.storage.sqlite_engine import SQLiteStorageEngine


def _legacy_schema(conn):
    conn.executescript(
        """
        CREATE TABLE notes (
            id TEXT PRIMARY KEY,
            type TEXT NOT NULL,
            lifecycle TEXT NOT NULL CHECK(lifecycle IN ('RAW','CLASSIFIED','NORMALIZED','REVIEW','VERIFIED','ACTIVE','SUPERSEDED','ARCHIVED')),
            category TEXT NOT NULL,
            tags TEXT NOT NULL,
            created TEXT NOT NULL,
            updated TEXT NOT NULL,
            source_type TEXT NOT NULL,
            source_ref TEXT NOT NULL,
            confidence TEXT NOT NULL,
            verification TEXT NOT NULL,
            valid_from TEXT,
            valid_until TEXT,
            version_range TEXT,
            applies_to TEXT,
            supersedes TEXT,
            superseded_by TEXT,
            conflicts_with TEXT,
            last_verified TEXT,
            verification_source TEXT,
            relations TEXT NOT NULL,
            provenance TEXT NOT NULL,
            content TEXT NOT NULL,
            raw_json TEXT NOT NULL
        );
        CREATE INDEX idx_notes_lifecycle ON notes(lifecycle);
        CREATE INDEX idx_notes_type ON notes(type);
        CREATE INDEX idx_notes_source_type ON notes(source_type);
        CREATE INDEX idx_notes_superseded_by ON notes(superseded_by);
        """
    )


def test_legacy_sqlite_schema_migrates_without_losing_rows_or_indexes(tmp_path):
    db_path = tmp_path / "legacy.sqlite"
    conn = sqlite3.connect(db_path)
    _legacy_schema(conn)
    raw = {
        "id": "legacy-1",
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "category": "legacy",
        "tags": [],
        "created": "2026-09-05",
        "updated": "2026-09-05",
        "provenance": {"source_type": "execution", "source_ref": "migration-test"},
        "confidence": "high",
        "verification": "verified",
        "relations": [],
        "content": "legacy",
    }
    conn.execute(
        "INSERT INTO notes VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)",
        (
            raw["id"], raw["type"], raw["lifecycle"], raw["category"], "[]", raw["created"], raw["updated"],
            "execution", "migration-test", raw["confidence"], raw["verification"], None, None, None, None, None,
            None, None, None, None, "[]", '{"source_type":"execution","source_ref":"migration-test"}',
            raw["content"], __import__("json").dumps(raw)
        ),
    )
    conn.commit()
    conn.close()

    storage = SQLiteStorageEngine(str(db_path))
    assert storage.get("legacy-1")["lifecycle"] == "ACTIVE"
    storage.set(
        "recon-1",
        {
            **raw,
            "id": "recon-1",
            "lifecycle": "RECONSOLIDATING",
            "content": "recon",
        },
    )
    assert storage.get("recon-1")["lifecycle"] == "RECONSOLIDATING"

    indexes = {
        row[0]
        for row in sqlite3.connect(db_path).execute(
            "SELECT name FROM sqlite_master WHERE type='index' AND name LIKE 'idx_notes_%'"
        )
    }
    assert indexes == {
        "idx_notes_lifecycle",
        "idx_notes_type",
        "idx_notes_source_type",
        "idx_notes_superseded_by",
    }
