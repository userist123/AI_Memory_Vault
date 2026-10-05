from __future__ import annotations

import sqlite3
import threading
import time
from pathlib import Path


class ReplayGuard:
    """Replay protection with bounded in-memory or persistent SQLite state."""

    def __init__(
        self,
        ttl_seconds: float = 180,
        max_entries: int = 100_000,
        max_ttl_seconds: float = 120,
        clock_skew_seconds: float = 30,
        path: str | Path | None = None,
    ):
        self.ttl_seconds = float(ttl_seconds)
        self.max_entries = int(max_entries)
        self.max_ttl_seconds = float(max_ttl_seconds)
        self.clock_skew_seconds = float(clock_skew_seconds)
        if self.ttl_seconds < self.max_ttl_seconds + self.clock_skew_seconds:
            raise ValueError("replay TTL must cover maximum token TTL plus clock skew")
        self.path = Path(path) if path else None
        self._entries: dict[tuple[str, str], float] = {}
        self._lock = threading.Lock()
        self._db = None
        if self.path:
            self.path.parent.mkdir(parents=True, exist_ok=True)
            self._db = sqlite3.connect(str(self.path), check_same_thread=False)
            self._db.execute(
                "CREATE TABLE IF NOT EXISTS replay_guard (task_id TEXT NOT NULL, nonce TEXT NOT NULL, seen_at REAL NOT NULL, PRIMARY KEY(task_id, nonce))"
            )
            self._db.commit()

    def claim(self, task_id: str, nonce: str, now: float | None = None) -> bool:
        current = time.time() if now is None else float(now)
        cutoff = current - self.ttl_seconds
        key = (task_id, nonce)
        with self._lock:
            if self._db is not None:
                self._db.execute("DELETE FROM replay_guard WHERE seen_at <= ?", (cutoff,))
                try:
                    self._db.execute(
                        "INSERT INTO replay_guard(task_id, nonce, seen_at) VALUES (?, ?, ?)",
                        (task_id, nonce, current),
                    )
                except sqlite3.IntegrityError:
                    self._db.rollback()
                    return False
                self._db.commit()
                return True
            self._entries = {k: t for k, t in self._entries.items() if t > cutoff}
            if key in self._entries:
                return False
            if len(self._entries) >= self.max_entries:
                oldest = min(self._entries, key=self._entries.get)
                del self._entries[oldest]
            self._entries[key] = current
            return True

    def close(self) -> None:
        if self._db is not None:
            with self._lock:
                self._db.close()
                self._db = None
