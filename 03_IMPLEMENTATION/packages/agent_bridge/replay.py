from __future__ import annotations

import threading
import time


class ReplayGuard:
    """Bounded in-memory replay guard for short-lived bridge capabilities."""

    def __init__(self, ttl_seconds: float = 300, max_entries: int = 100_000):
        self.ttl_seconds = float(ttl_seconds)
        self.max_entries = int(max_entries)
        self._entries: dict[tuple[str, str], float] = {}
        self._lock = threading.Lock()

    def claim(self, task_id: str, nonce: str, now: float | None = None) -> bool:
        current = time.time() if now is None else float(now)
        key = (task_id, nonce)
        with self._lock:
            cutoff = current - self.ttl_seconds
            self._entries = {k: t for k, t in self._entries.items() if t > cutoff}
            if key in self._entries:
                return False
            if len(self._entries) >= self.max_entries:
                oldest = min(self._entries, key=self._entries.get)
                del self._entries[oldest]
            self._entries[key] = current
            return True
