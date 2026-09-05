import sqlite3
import json
import os
import threading
from typing import Dict, Any, List, Optional

class SQLiteStorageEngine:
    """Production SQLite storage engine with Write-Ahead Logging (WAL),
    strict schema constraints, atomic transactions via BEGIN IMMEDIATE,
    and thread-safe connection handling.
    """

    SCHEMA = """
    CREATE TABLE IF NOT EXISTS notes (
        id TEXT PRIMARY KEY,
        type TEXT NOT NULL CHECK(type IN ('knowledge', 'project', 'procedure', 'decision', 'experience', 'error', 'lesson', 'preference', 'resource', 'hypothesis', 'system', 'core', 'index')),
        lifecycle TEXT NOT NULL CHECK(lifecycle IN ('RAW', 'CLASSIFIED', 'NORMALIZED', 'REVIEW', 'VERIFIED', 'ACTIVE', 'RECONSOLIDATING', 'SUPERSEDED', 'ARCHIVED')),
        category TEXT NOT NULL,
        tags TEXT NOT NULL,
        created TEXT NOT NULL,
        updated TEXT NOT NULL,
        source_type TEXT NOT NULL CHECK(source_type IN ('user', 'official', 'execution', 'experience', 'ai', 'inference', 'import', 'unknown')),
        source_ref TEXT NOT NULL,
        confidence TEXT NOT NULL CHECK(confidence IN ('very_high', 'high', 'medium', 'low', 'unknown')),
        verification TEXT NOT NULL CHECK(verification IN ('verified', 'partially_verified', 'unverified', 'inferred')),
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
    CREATE INDEX IF NOT EXISTS idx_notes_lifecycle ON notes(lifecycle);
    CREATE INDEX IF NOT EXISTS idx_notes_type ON notes(type);
    CREATE INDEX IF NOT EXISTS idx_notes_source_type ON notes(source_type);
    CREATE INDEX IF NOT EXISTS idx_notes_superseded_by ON notes(superseded_by);
    """

    _NOTE_COLUMNS = (
        "id, type, lifecycle, category, tags, created, updated, "
        "source_type, source_ref, confidence, verification, valid_from, valid_until, "
        "version_range, applies_to, supersedes, superseded_by, conflicts_with, "
        "last_verified, verification_source, relations, provenance, content, raw_json"
    )
    _NOTE_INDEXES = (
        "CREATE INDEX IF NOT EXISTS idx_notes_lifecycle ON notes(lifecycle)",
        "CREATE INDEX IF NOT EXISTS idx_notes_type ON notes(type)",
        "CREATE INDEX IF NOT EXISTS idx_notes_source_type ON notes(source_type)",
        "CREATE INDEX IF NOT EXISTS idx_notes_superseded_by ON notes(superseded_by)",
    )

    def __init__(self, db_path: str = ":memory:", timeout: float = 5.0, wal_mode: bool = True):
        self.db_path = db_path
        self.timeout = timeout
        self.wal_mode = wal_mode and db_path != ":memory:"
        self._local = threading.local()
        self._all_connections: List[sqlite3.Connection] = []
        self._lock = threading.Lock()
        if db_path != ":memory:":
            os.makedirs(os.path.dirname(os.path.abspath(db_path)), exist_ok=True)
        conn = self._get_connection()
        with conn:
            conn.executescript(self.SCHEMA)
        self._migrate_legacy_lifecycle_constraint(conn)

    def _migrate_legacy_lifecycle_constraint(self, conn: sqlite3.Connection) -> None:
        """Rebuild a legacy notes table whose lifecycle CHECK predates RECONSOLIDATING."""
        row = conn.execute(
            "SELECT sql FROM sqlite_master WHERE type='table' AND name='notes'"
        ).fetchone()
        schema_sql = row[0] if row and row[0] else ""
        if "RECONSOLIDATING" in schema_sql:
            return

        try:
            conn.execute("PRAGMA foreign_keys=OFF;")
            conn.execute("BEGIN IMMEDIATE;")
            for index_name in (
                "idx_notes_lifecycle",
                "idx_notes_type",
                "idx_notes_source_type",
                "idx_notes_superseded_by",
            ):
                conn.execute(f"DROP INDEX IF EXISTS {index_name}")
            conn.execute("ALTER TABLE notes RENAME TO notes_legacy_lifecycle;")
            conn.execute(
                """CREATE TABLE notes (
                    id TEXT PRIMARY KEY,
                    type TEXT NOT NULL CHECK(type IN ('knowledge', 'project', 'procedure', 'decision', 'experience', 'error', 'lesson', 'preference', 'resource', 'hypothesis', 'system', 'core', 'index')),
                    lifecycle TEXT NOT NULL CHECK(lifecycle IN ('RAW', 'CLASSIFIED', 'NORMALIZED', 'REVIEW', 'VERIFIED', 'ACTIVE', 'RECONSOLIDATING', 'SUPERSEDED', 'ARCHIVED')),
                    category TEXT NOT NULL,
                    tags TEXT NOT NULL,
                    created TEXT NOT NULL,
                    updated TEXT NOT NULL,
                    source_type TEXT NOT NULL CHECK(source_type IN ('user', 'official', 'execution', 'experience', 'ai', 'inference', 'import', 'unknown')),
                    source_ref TEXT NOT NULL,
                    confidence TEXT NOT NULL CHECK(confidence IN ('very_high', 'high', 'medium', 'low', 'unknown')),
                    verification TEXT NOT NULL CHECK(verification IN ('verified', 'partially_verified', 'unverified', 'inferred')),
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
                )"""
            )
            conn.execute(
                f"INSERT INTO notes ({self._NOTE_COLUMNS}) SELECT {self._NOTE_COLUMNS} FROM notes_legacy_lifecycle"
            )
            conn.execute("DROP TABLE notes_legacy_lifecycle;")
            for index_sql in self._NOTE_INDEXES:
                conn.execute(index_sql)
            conn.execute("COMMIT;")
        except Exception:
            try:
                conn.execute("ROLLBACK;")
            except Exception:
                pass
            raise
        finally:
            conn.execute("PRAGMA foreign_keys=ON;")

    def _get_connection(self) -> sqlite3.Connection:
        """Returns a thread-local SQLite connection configured with required PRAGMAs."""
        if not hasattr(self._local, "conn") or self._local.conn is None:
            conn = sqlite3.connect(
                self.db_path,
                timeout=self.timeout,
                isolation_level=None,
                check_same_thread=False
            )
            conn.row_factory = sqlite3.Row
            if self.wal_mode:
                conn.execute("PRAGMA journal_mode=WAL;")
            conn.execute("PRAGMA synchronous=NORMAL;")
            conn.execute("PRAGMA busy_timeout=5000;")
            conn.execute("PRAGMA foreign_keys=ON;")
            self._local.conn = conn
            with self._lock:
                self._all_connections.append(conn)
        return self._local.conn

    def get(self, note_id: str) -> Optional[Dict[str, Any]]:
        conn = self._get_connection()
        cursor = conn.execute("SELECT raw_json FROM notes WHERE id = ?", (str(note_id),))
        row = cursor.fetchone()
        if row is None:
            return None
        return json.loads(row["raw_json"])

    def set(self, note_id: str, data: Dict[str, Any]) -> None:
        yaml_id = data.get("id")
        if str(note_id) != str(yaml_id):
            raise ValueError(f"ID mismatch: storage key '{note_id}' must equal note id '{yaml_id}'")
        conn = self._get_connection()
        provenance = data.get("provenance", {})
        source_type = provenance.get("source_type", "unknown")
        source_ref = provenance.get("source_ref", "unknown")
        tags_json = json.dumps(data.get("tags", []))
        relations_json = json.dumps(data.get("relations", []))
        provenance_json = json.dumps(provenance)
        raw_json = json.dumps(data)
        content = data.get("content", "")
        content_str = json.dumps(content) if isinstance(content, (dict, list)) else (str(content) if content is not None else "")
        insert_sql = """
        INSERT INTO notes (
            id, type, lifecycle, category, tags, created, updated,
            source_type, source_ref, confidence, verification,
            valid_from, valid_until, version_range, applies_to,
            supersedes, superseded_by, conflicts_with,
            last_verified, verification_source,
            relations, provenance, content, raw_json
        ) VALUES (
            ?, ?, ?, ?, ?, ?, ?,
            ?, ?, ?, ?,
            ?, ?, ?, ?,
            ?, ?, ?,
            ?, ?,
            ?, ?, ?, ?
        )
        ON CONFLICT(id) DO UPDATE SET
            type = excluded.type,
            lifecycle = excluded.lifecycle,
            category = excluded.category,
            tags = excluded.tags,
            created = excluded.created,
            updated = excluded.updated,
            source_type = excluded.source_type,
            source_ref = excluded.source_ref,
            confidence = excluded.confidence,
            verification = excluded.verification,
            valid_from = excluded.valid_from,
            valid_until = excluded.valid_until,
            version_range = excluded.version_range,
            applies_to = excluded.applies_to,
            supersedes = excluded.supersedes,
            superseded_by = excluded.superseded_by,
            conflicts_with = excluded.conflicts_with,
            last_verified = excluded.last_verified,
            verification_source = excluded.verification_source,
            relations = excluded.relations,
            provenance = excluded.provenance,
            content = excluded.content,
            raw_json = excluded.raw_json;
        """
        params = (
            str(note_id), data.get("type", "knowledge"), data.get("lifecycle", "RAW"), data.get("category", "general"),
            tags_json, data.get("created", ""), data.get("updated", ""), source_type, source_ref,
            data.get("confidence", "unknown"), data.get("verification", "unverified"), data.get("valid_from"),
            data.get("valid_until"), data.get("version_range"), data.get("applies_to"), data.get("supersedes"),
            data.get("superseded_by"), data.get("conflicts_with"), data.get("last_verified"), data.get("verification_source"),
            relations_json, provenance_json, content_str, raw_json
        )
        try:
            conn.execute("BEGIN IMMEDIATE;")
            conn.execute(insert_sql, params)
            conn.execute("COMMIT;")
        except Exception as e:
            try:
                conn.execute("ROLLBACK;")
            except Exception:
                pass
            raise e

    def delete(self, note_id: str) -> None:
        conn = self._get_connection()
        try:
            conn.execute("BEGIN IMMEDIATE;")
            conn.execute("DELETE FROM notes WHERE id = ?", (str(note_id),))
            conn.execute("COMMIT;")
        except Exception as e:
            try:
                conn.execute("ROLLBACK;")
            except Exception:
                pass
            raise e

    def query(self, intent: str = None, lifecycle: List[str] = None, types: List[str] = None) -> List[Dict[str, Any]]:
        conn = self._get_connection()
        query_sql = "SELECT raw_json FROM notes WHERE lifecycle != 'RAW'"
        params = []
        if lifecycle:
            placeholders = ",".join("?" for _ in lifecycle)
            query_sql += f" AND lifecycle IN ({placeholders})"
            params.extend(lifecycle)
        if types:
            placeholders = ",".join("?" for _ in types)
            query_sql += f" AND type IN ({placeholders})"
            params.extend(types)
        cursor = conn.execute(query_sql, params)
        return [json.loads(row["raw_json"]) for row in cursor.fetchall()]

    def all_notes(self) -> List[Dict[str, Any]]:
        """Return all non-RAW notes for read-only indexing consumers."""
        conn = self._get_connection()
        cursor = conn.execute("SELECT raw_json FROM notes WHERE lifecycle != 'RAW'")
        return [json.loads(row["raw_json"]) for row in cursor.fetchall()]

    def resolve_active_lineage(self, note_id: str) -> str:
        conn = self._get_connection()
        cte_sql = """
        WITH RECURSIVE lineage(current_id, next_id, depth) AS (
            SELECT id, superseded_by, 0 FROM notes WHERE id = ?
            UNION ALL
            SELECT n.id, n.superseded_by, l.depth + 1
            FROM notes n JOIN lineage l ON n.id = l.next_id
            WHERE l.next_id IS NOT NULL AND l.depth < 50
        )
        SELECT current_id FROM lineage ORDER BY depth DESC LIMIT 1;
        """
        cursor = conn.execute(cte_sql, (str(note_id),))
        row = cursor.fetchone()
        return row["current_id"] if row else str(note_id)

    def checkpoint(self, mode: str = "TRUNCATE") -> None:
        if self.wal_mode:
            self._get_connection().execute(f"PRAGMA wal_checkpoint({mode});")

    def close(self) -> None:
        with self._lock:
            for conn in self._all_connections:
                try:
                    conn.close()
                except Exception:
                    pass
            self._all_connections.clear()
            if hasattr(self._local, "conn"):
                self._local.conn = None
