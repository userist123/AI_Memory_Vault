"""
Thread-safe SQLite Storage Engine with WAL Mode and Recursive CTE Lineage Traversal.
"""

import sqlite3
import threading
import json
import os
from pathlib import Path
from typing import Optional, Dict, Any, List, Union

from jarvis.memory.invariants import (
    Principal,
    Lifecycle,
    NoteType,
    validate_propose_invariants,
    validate_update_invariants,
    validate_attest_invariants,
    validate_promote_invariants,
    validate_supersession_invariants,
)


class SQLiteStorageEngine:
    """Thread-safe SQLite storage engine in WAL mode with atomic transactions."""

    SCHEMA = """
    CREATE TABLE IF NOT EXISTS notes (
        id TEXT PRIMARY KEY,
        type TEXT NOT NULL CHECK(type IN ('knowledge', 'project', 'procedure', 'decision', 'experience', 'error', 'lesson', 'preference', 'resource', 'hypothesis', 'system', 'core')),
        lifecycle TEXT NOT NULL CHECK(lifecycle IN ('RAW', 'CLASSIFIED', 'NORMALIZED', 'REVIEW', 'VERIFIED', 'ACTIVE', 'RECONSOLIDATING', 'SUPERSEDED', 'ARCHIVED')),
        category TEXT NOT NULL,
        tags TEXT NOT NULL,
        created TEXT NOT NULL,
        updated TEXT NOT NULL,
        source_type TEXT NOT NULL,
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
        relations TEXT NOT NULL,
        provenance TEXT NOT NULL,
        content TEXT NOT NULL,
        raw_json TEXT NOT NULL
    );

    CREATE INDEX IF NOT EXISTS idx_notes_lifecycle ON notes(lifecycle);
    CREATE INDEX IF NOT EXISTS idx_notes_type ON notes(type);
    CREATE INDEX IF NOT EXISTS idx_notes_supersedes ON notes(supersedes);
    CREATE INDEX IF NOT EXISTS idx_notes_superseded_by ON notes(superseded_by);

    CREATE TABLE IF NOT EXISTS audit_trail (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        timestamp TEXT NOT NULL,
        operation TEXT NOT NULL,
        principal TEXT NOT NULL,
        target_id TEXT NOT NULL,
        success INTEGER NOT NULL,
        prev_hash TEXT NOT NULL,
        entry_hash TEXT NOT NULL,
        details TEXT NOT NULL
    );
    """

    def __init__(
        self,
        db_path: Union[str, Path] = "vault_memory.sqlite3",
        timeout: float = 10.0,
        wal_mode: bool = True,
    ):
        self.db_path = str(db_path)
        self.timeout = timeout
        self.wal_mode = wal_mode
        self._local = threading.local()

        # Ensure directory exists
        db_dir = os.path.dirname(os.path.abspath(self.db_path))
        if db_dir:
            os.makedirs(db_dir, exist_ok=True)

        # Initialize schema
        conn = self._get_conn()
        with conn:
            conn.executescript(self.SCHEMA)

    def _get_conn(self) -> sqlite3.Connection:
        """Get or initialize thread-local connection with WAL mode and pragmas."""
        if not hasattr(self._local, "conn") or self._local.conn is None:
            conn = sqlite3.connect(
                self.db_path,
                timeout=self.timeout,
                isolation_level=None,  # Autocommit mode; we manage explicit BEGIN IMMEDIATE
                check_same_thread=False,
            )
            conn.row_factory = sqlite3.Row
            if self.wal_mode:
                conn.execute("PRAGMA journal_mode=WAL;")
            conn.execute("PRAGMA busy_timeout=5000;")
            conn.execute("PRAGMA synchronous=NORMAL;")
            conn.execute("PRAGMA foreign_keys=ON;")
            conn.execute("PRAGMA mmap_size=268435456;")
            self._local.conn = conn
        return self._local.conn

    def close(self) -> None:
        """Close thread-local connection."""
        if hasattr(self._local, "conn") and self._local.conn is not None:
            try:
                self._local.conn.close()
            except Exception:
                pass
            self._local.conn = None

    def _row_to_dict(self, row: sqlite3.Row) -> Dict[str, Any]:
        """Convert a database row back to note dictionary representation."""
        raw_json_str = row["raw_json"]
        if raw_json_str:
            try:
                note_dict = json.loads(raw_json_str)
                # Ensure fields from row are synced
                note_dict["id"] = row["id"]
                note_dict["type"] = row["type"]
                note_dict["lifecycle"] = row["lifecycle"]
                note_dict["category"] = row["category"]
                note_dict["created"] = row["created"]
                note_dict["updated"] = row["updated"]
                note_dict["confidence"] = row["confidence"]
                note_dict["verification"] = row["verification"]
                note_dict["supersedes"] = row["supersedes"]
                note_dict["superseded_by"] = row["superseded_by"]
                note_dict["conflicts_with"] = row["conflicts_with"]
                note_dict["content"] = row["content"]
                return note_dict
            except Exception:
                pass

        # Fallback manual reconstruction
        return {
            "id": row["id"],
            "type": row["type"],
            "lifecycle": row["lifecycle"],
            "category": row["category"],
            "tags": json.loads(row["tags"]) if row["tags"] else [],
            "created": row["created"],
            "updated": row["updated"],
            "provenance": json.loads(row["provenance"]) if row["provenance"] else {},
            "confidence": row["confidence"],
            "verification": row["verification"],
            "valid_from": row["valid_from"],
            "valid_until": row["valid_until"],
            "version_range": row["version_range"],
            "applies_to": row["applies_to"],
            "supersedes": row["supersedes"],
            "superseded_by": row["superseded_by"],
            "conflicts_with": row["conflicts_with"],
            "relations": json.loads(row["relations"]) if row["relations"] else [],
            "content": row["content"],
        }

    def get(self, note_id: str) -> Optional[Dict[str, Any]]:
        """Retrieve note by ID."""
        conn = self._get_conn()
        cursor = conn.execute("SELECT * FROM notes WHERE id = ?", (note_id,))
        row = cursor.fetchone()
        return self._row_to_dict(row) if row else None

    def propose(self, principal: Principal, note: Dict[str, Any]) -> Dict[str, Any]:
        """Propose a new memory note subject to P0-P18 invariants."""
        validate_propose_invariants(principal, note)
        self.set_note_atomic(note)
        return note

    def update(self, principal: Principal, note_id: str, updates: Dict[str, Any]) -> Dict[str, Any]:
        """Update an existing note subject to immutability invariants."""
        current = self.get(note_id)
        if not current:
            raise ValueError(f"Note with ID '{note_id}' does not exist.")
        validate_update_invariants(principal, current, updates)

        # Merge updates
        merged = current.copy()
        for k, v in updates.items():
            if k == "provenance" and isinstance(v, dict):
                merged_prov = merged.get("provenance", {}).copy()
                merged_prov.update(v)
                merged["provenance"] = merged_prov
            else:
                merged[k] = v

        self.set_note_atomic(merged)
        return merged

    def attest(self, principal: Principal, note_id: str, reason: str = "", evidence_ref: str = "") -> Dict[str, Any]:
        """Attest memory note, promoting verification status to 'verified'."""
        validate_attest_invariants(principal, note_id)
        current = self.get(note_id)
        if not current:
            raise ValueError(f"Note with ID '{note_id}' does not exist.")

        current["verification"] = "verified"
        current["attestation_reason"] = reason
        current["attestation_evidence"] = evidence_ref
        self.set_note_atomic(current)
        return current

    def promote(self, principal: Principal, note_id: str) -> Dict[str, Any]:
        """Promote note from REVIEW to ACTIVE."""
        current = self.get(note_id)
        if not current:
            raise ValueError(f"Note with ID '{note_id}' does not exist.")
        validate_promote_invariants(principal, current)

        current["lifecycle"] = Lifecycle.ACTIVE.value
        self.set_note_atomic(current)
        return current

    def archive(self, principal: Principal, note_id: str, reason: str = "") -> Dict[str, Any]:
        """Archive a note."""
        current = self.get(note_id)
        if not current:
            raise ValueError(f"Note with ID '{note_id}' does not exist.")

        current["lifecycle"] = Lifecycle.ARCHIVED.value
        current["archive_reason"] = reason
        self.set_note_atomic(current)
        return current

    def supersede(self, principal: Principal, old_id: str, new_id: str) -> None:
        """Atomic 2-node supersession operation enforcing reciprocal links and DAG acyclicity."""
        old_note = self.get(old_id)
        new_note = self.get(new_id)
        if not old_note or not new_note:
            raise ValueError("Both old_id and new_id must exist in storage to supersede.")

        # Fetch lineage to detect multi-hop cycles
        lineage = self.get_lineage(old_id)
        ancestor_ids = {n["id"] for n in lineage if n["id"] != old_id}

        validate_supersession_invariants(old_note, new_note, ancestor_ids=ancestor_ids)

        old_note["lifecycle"] = Lifecycle.SUPERSEDED.value
        old_note["superseded_by"] = new_id
        new_note["supersedes"] = old_id

        conn = self._get_conn()
        try:
            conn.execute("BEGIN IMMEDIATE;")
            self._write_note_in_transaction(conn, old_note)
            self._write_note_in_transaction(conn, new_note)
            conn.execute("COMMIT;")
        except Exception as e:
            try:
                conn.execute("ROLLBACK;")
            except Exception:
                pass
            raise e

    def set_note_atomic(self, note: Dict[str, Any]) -> None:
        """Persist or update note atomically with BEGIN IMMEDIATE transaction."""
        conn = self._get_conn()
        try:
            conn.execute("BEGIN IMMEDIATE;")
            self._write_note_in_transaction(conn, note)
            conn.execute("COMMIT;")
        except Exception as e:
            try:
                conn.execute("ROLLBACK;")
            except Exception:
                pass
            raise e

    def _write_note_in_transaction(self, conn: sqlite3.Connection, note: Dict[str, Any]) -> None:
        """Execute INSERT OR REPLACE within an existing transaction."""
        insert_sql = """
        INSERT INTO notes (
            id, type, lifecycle, category, tags, created, updated,
            source_type, source_ref, confidence, verification,
            valid_from, valid_until, version_range, applies_to,
            supersedes, superseded_by, conflicts_with,
            relations, provenance, content, raw_json
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        ON CONFLICT(id) DO UPDATE SET
            type=excluded.type,
            lifecycle=excluded.lifecycle,
            category=excluded.category,
            tags=excluded.tags,
            updated=excluded.updated,
            source_type=excluded.source_type,
            source_ref=excluded.source_ref,
            confidence=excluded.confidence,
            verification=excluded.verification,
            valid_from=excluded.valid_from,
            valid_until=excluded.valid_until,
            version_range=excluded.version_range,
            applies_to=excluded.applies_to,
            supersedes=excluded.supersedes,
            superseded_by=excluded.superseded_by,
            conflicts_with=excluded.conflicts_with,
            relations=excluded.relations,
            provenance=excluded.provenance,
            content=excluded.content,
            raw_json=excluded.raw_json;
        """
        prov = note.get("provenance", {})
        source_type = prov.get("source_type", "unknown") if isinstance(prov, dict) else getattr(prov, "source_type", "unknown")
        source_ref = prov.get("source_ref", "unknown") if isinstance(prov, dict) else getattr(prov, "source_ref", "unknown")

        lifecycle = note.get("lifecycle", "REVIEW")
        if isinstance(lifecycle, Lifecycle):
            lifecycle = lifecycle.value

        note_type = note.get("type", "knowledge")
        if isinstance(note_type, NoteType):
            note_type = note_type.value

        tags = note.get("tags", [])
        tags_json = json.dumps(tags) if isinstance(tags, (list, dict)) else str(tags)
        relations = note.get("relations", [])
        relations_json = json.dumps(relations) if isinstance(relations, (list, dict)) else str(relations)
        prov_json = json.dumps(prov) if isinstance(prov, (list, dict)) else str(prov)

        raw_copy = note.copy()
        raw_copy["lifecycle"] = lifecycle
        raw_copy["type"] = note_type
        raw_json = json.dumps(raw_copy)

        params = (
            note["id"],
            note_type,
            lifecycle,
            note.get("category", "general"),
            tags_json,
            note.get("created", ""),
            note.get("updated", ""),
            source_type,
            source_ref,
            note.get("confidence", "medium"),
            note.get("verification", "unverified"),
            note.get("valid_from"),
            note.get("valid_until"),
            note.get("version_range"),
            note.get("applies_to"),
            note.get("supersedes"),
            note.get("superseded_by"),
            note.get("conflicts_with"),
            relations_json,
            prov_json,
            note.get("content", ""),
            raw_json,
        )
        conn.execute(insert_sql, params)

    def delete(self, note_id: str) -> bool:
        """Delete note by ID."""
        conn = self._get_conn()
        try:
            conn.execute("BEGIN IMMEDIATE;")
            cursor = conn.execute("DELETE FROM notes WHERE id = ?", (note_id,))
            deleted = cursor.rowcount > 0
            conn.execute("COMMIT;")
            return deleted
        except Exception as e:
            try:
                conn.execute("ROLLBACK;")
            except Exception:
                pass
            raise e

    def query(
        self,
        lifecycle: Optional[Union[str, List[str]]] = None,
        note_type: Optional[Union[str, List[str]]] = None,
        category: Optional[str] = None,
        limit: int = 100,
    ) -> List[Dict[str, Any]]:
        """Query notes by metadata filters."""
        conn = self._get_conn()
        clauses = []
        params: List[Any] = []

        if lifecycle:
            if isinstance(lifecycle, str):
                clauses.append("lifecycle = ?")
                params.append(lifecycle)
            elif isinstance(lifecycle, list):
                placeholders = ",".join(["?"] * len(lifecycle))
                clauses.append(f"lifecycle IN ({placeholders})")
                params.extend(lifecycle)

        if note_type:
            if isinstance(note_type, str):
                clauses.append("type = ?")
                params.append(note_type)
            elif isinstance(note_type, list):
                placeholders = ",".join(["?"] * len(note_type))
                clauses.append(f"type IN ({placeholders})")
                params.extend(note_type)

        if category:
            clauses.append("category = ?")
            params.append(category)

        where_sql = f"WHERE {' AND '.join(clauses)}" if clauses else ""
        sql = f"SELECT * FROM notes {where_sql} ORDER BY updated DESC LIMIT ?"
        params.append(limit)

        cursor = conn.execute(sql, params)
        rows = cursor.fetchall()
        return [self._row_to_dict(r) for r in rows]

    def search_bm25(self, query: str, limit: int = 20) -> List[Dict[str, Any]]:
        """Lexical search matching keywords in content, tags, category, and source_ref."""
        conn = self._get_conn()
        raw_tokens = [t.strip().lower() for t in query.split() if len(t.strip()) > 1]
        # Deduplicate while preserving order and limit to max 32 tokens to prevent SQLite tree depth overflow
        tokens = list(dict.fromkeys(raw_tokens))[:32]
        if not tokens:
            return self.query(limit=limit)

        # Build parameterized LIKE filters
        clauses = []
        params = []
        for token in tokens:
            pattern = f"%{token}%"
            clauses.append("(LOWER(content) LIKE ? OR LOWER(category) LIKE ? OR LOWER(tags) LIKE ? OR LOWER(source_ref) LIKE ?)")
            params.extend([pattern, pattern, pattern, pattern])

        where_sql = "WHERE " + " OR ".join(clauses)
        sql = f"SELECT * FROM notes {where_sql} LIMIT ?"
        params.append(limit)

        cursor = conn.execute(sql, params)
        rows = cursor.fetchall()
        return [self._row_to_dict(r) for r in rows]

    def get_lineage(self, note_id: str, max_depth: int = 50) -> List[Dict[str, Any]]:
        """
        Recursive CTE lineage traversal.
        Traces supersession chains forward and backward up to max_depth.
        """
        conn = self._get_conn()
        cte_sql = """
        WITH RECURSIVE lineage_forward(id, depth) AS (
            SELECT id, 0 FROM notes WHERE id = ?
            UNION
            SELECT n.superseded_by, lf.depth + 1
            FROM notes n
            JOIN lineage_forward lf ON n.id = lf.id
            WHERE n.superseded_by IS NOT NULL AND lf.depth < ?
        ),
        lineage_backward(id, depth) AS (
            SELECT id, 0 FROM notes WHERE id = ?
            UNION
            SELECT n.supersedes, lb.depth + 1
            FROM notes n
            JOIN lineage_backward lb ON n.id = lb.id
            WHERE n.supersedes IS NOT NULL AND lb.depth < ?
        )
        SELECT DISTINCT n.*
        FROM notes n
        WHERE n.id IN (SELECT id FROM lineage_forward UNION SELECT id FROM lineage_backward);
        """
        cursor = conn.execute(cte_sql, (note_id, max_depth, note_id, max_depth))
        rows = cursor.fetchall()
        return [self._row_to_dict(r) for r in rows]

    def resolve_active_lineage(self, note_id: str) -> Optional[Dict[str, Any]]:
        """Find the active successor note at the head of a supersession chain."""
        lineage = self.get_lineage(note_id)
        # Search for node that is ACTIVE and not superseded by another node in the chain
        for note in lineage:
            if note.get("lifecycle") == "ACTIVE" and not note.get("superseded_by"):
                return note
        # Fallback: any ACTIVE note
        for note in lineage:
            if note.get("lifecycle") == "ACTIVE":
                return note
        return None

    def count(self) -> int:
        """Return total number of notes."""
        conn = self._get_conn()
        cursor = conn.execute("SELECT COUNT(*) FROM notes")
        return cursor.fetchone()[0]
