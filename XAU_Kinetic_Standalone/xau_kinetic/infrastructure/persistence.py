"""
SQLite Persistence Engine operating in WAL mode.
Provides tick market data storage and SHA-256 tamper-evident audit event chaining
in compliance with AI Memory Vault integrity invariants.
"""

import hashlib
import json
import logging
import sqlite3
import threading
import uuid
from contextlib import contextmanager
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from xau_kinetic.application.interfaces import IPersistence
from xau_kinetic.domain.models import TickData, AuditEvent

logger = logging.getLogger("xau_kinetic.persistence")


class SQLitePersistence(IPersistence):
    """SQLite WAL Mode Persistence Engine with SHA-256 Chained Hash Ledger."""

    GENESIS_HASH = "0000000000000000000000000000000000000000000000000000000000000000"

    def __init__(self, db_path: str | Path = "xau_kinetic_audit.db") -> None:
        self.db_path = str(db_path)
        self._audit_lock = threading.Lock()
        self._init_database()

    @contextmanager
    def _db_connection(self):
        """Create sqlite3 connection in WAL mode and ensure it is closed on exit."""
        conn = sqlite3.connect(self.db_path, timeout=10.0)
        conn.execute("PRAGMA journal_mode=WAL;")
        conn.execute("PRAGMA synchronous=NORMAL;")
        conn.execute("PRAGMA busy_timeout=5000;")
        conn.row_factory = sqlite3.Row
        try:
            yield conn
        finally:
            conn.close()

    def _init_database(self) -> None:
        """Initialize database schema tables for ticks and chained audit log."""
        with self._db_connection() as conn:
            # Ticks table
            conn.execute(
                """
                CREATE TABLE IF NOT EXISTS ticks (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    symbol TEXT NOT NULL,
                    bid REAL NOT NULL,
                    ask REAL NOT NULL,
                    last REAL NOT NULL,
                    volume REAL NOT NULL,
                    timestamp TEXT NOT NULL
                );
                """
            )
            conn.execute("CREATE INDEX IF NOT EXISTS idx_ticks_symbol_ts ON ticks(symbol, timestamp);")

            # Audit log table chained with SHA-256
            conn.execute(
                """
                CREATE TABLE IF NOT EXISTS audit_log (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    event_id TEXT NOT NULL UNIQUE,
                    timestamp TEXT NOT NULL,
                    event_type TEXT NOT NULL,
                    payload TEXT NOT NULL,
                    prev_hash TEXT NOT NULL,
                    current_hash TEXT NOT NULL
                );
                """
            )
            conn.commit()

    def save_ticks(self, ticks: list[TickData]) -> None:
        """Persist a list of TickData objects."""
        if not ticks:
            return

        records = [
            (
                t.symbol,
                t.bid,
                t.ask,
                t.last,
                t.volume,
                t.timestamp.isoformat(),
            )
            for t in ticks
        ]
        with self._db_connection() as conn:
            conn.executemany(
                """
                INSERT INTO ticks (symbol, bid, ask, last, volume, timestamp)
                VALUES (?, ?, ?, ?, ?, ?);
                """,
                records,
            )
            conn.commit()

    def get_last_audit_hash(self) -> str:
        """Fetch the current head hash from the audit log chain."""
        with self._db_connection() as conn:
            cursor = conn.execute("SELECT current_hash FROM audit_log ORDER BY id DESC LIMIT 1;")
            row = cursor.fetchone()
            if row and row["current_hash"]:
                return str(row["current_hash"])
            return self.GENESIS_HASH

    def log_audit_event(self, event_type: str, payload: dict[str, Any]) -> AuditEvent:
        """
        Thread-safe execution: Record audit event into SHA-256 chained hash ledger.
        Ensures cryptographic tamper-evidence: current_hash = SHA256(prev_hash + timestamp + event_type + json_payload).
        """
        with self._audit_lock:
            with self._db_connection() as conn:
                cursor = conn.execute("SELECT current_hash FROM audit_log ORDER BY id DESC LIMIT 1;")
                row = cursor.fetchone()
                prev_hash = str(row["current_hash"]) if (row and row["current_hash"]) else self.GENESIS_HASH

                event_id = str(uuid.uuid4())
                now = datetime.now(timezone.utc)
                iso_timestamp = now.isoformat()
                payload_str = json.dumps(payload, sort_keys=True)

                # Compute SHA-256 digest
                digest_input = f"{prev_hash}|{iso_timestamp}|{event_type}|{payload_str}".encode("utf-8")
                current_hash = hashlib.sha256(digest_input).hexdigest()

                event = AuditEvent(
                    event_id=event_id,
                    timestamp=now,
                    event_type=event_type,
                    payload=payload,
                    prev_hash=prev_hash,
                    current_hash=current_hash,
                )

                conn.execute(
                    """
                    INSERT INTO audit_log (event_id, timestamp, event_type, payload, prev_hash, current_hash)
                    VALUES (?, ?, ?, ?, ?, ?);
                    """,
                    (
                        event.event_id,
                        iso_timestamp,
                        event.event_type,
                        payload_str,
                        event.prev_hash,
                        event.current_hash,
                    ),
                )
                conn.commit()

                logger.debug(f"Audit Logged [{event_type}]: {current_hash[:8]}... (prev: {prev_hash[:8]}...)")
                return event

    def verify_chain_integrity(self) -> tuple[bool, str]:
        """Verify the complete SHA-256 chain integrity of the audit log."""
        with self._db_connection() as conn:
            cursor = conn.execute(
                "SELECT event_id, timestamp, event_type, payload, prev_hash, current_hash FROM audit_log ORDER BY id ASC;"
            )
            rows = cursor.fetchall()

        expected_prev = self.GENESIS_HASH
        for i, r in enumerate(rows):
            if r["prev_hash"] != expected_prev:
                return False, f"Broken chain at row {i+1} (event {r['event_id']}): expected prev_hash {expected_prev}, found {r['prev_hash']}"

            payload_str = r["payload"]
            digest_input = f"{r['prev_hash']}|{r['timestamp']}|{r['event_type']}|{payload_str}".encode("utf-8")
            calc_hash = hashlib.sha256(digest_input).hexdigest()
            if calc_hash != r["current_hash"]:
                return False, f"Hash mismatch at row {i+1} (event {r['event_id']}): calculated {calc_hash}, stored {r['current_hash']}"

            expected_prev = r["current_hash"]

        return True, "Chain integrity verified valid."
