"""Ephemeral, bounded per-session input buffer for Memory V6."""
from __future__ import annotations

from dataclasses import asdict, dataclass
from datetime import datetime, timedelta, timezone
from hashlib import sha256
from typing import Dict, List, Optional
import uuid


@dataclass(frozen=True)
class SensorEvent:
    event_id: str
    session_id: str
    agent_id: str
    role: str
    content: str
    created_at: str
    source_ref: str
    content_hash: str


class SensorBuffer:
    """In-memory session buffer. It never writes canonical memory."""

    def __init__(self, max_events_per_session: int = 100, ttl_minutes: int = 120):
        if max_events_per_session < 1 or ttl_minutes < 1:
            raise ValueError("max_events_per_session and ttl_minutes must be positive")
        self.max_events_per_session = max_events_per_session
        self.ttl = timedelta(minutes=ttl_minutes)
        self._events: Dict[str, List[SensorEvent]] = {}

    @staticmethod
    def _now() -> datetime:
        return datetime.now(timezone.utc)

    def _purge(self, session_id: str) -> None:
        cutoff = self._now() - self.ttl
        self._events[session_id] = [
            item for item in self._events.get(session_id, [])
            if datetime.fromisoformat(item.created_at) >= cutoff
        ]

    def append(self, session_id: str, agent_id: str, role: str, content: str,
               source_ref: Optional[str] = None) -> SensorEvent:
        if not session_id.strip() or not agent_id.strip() or not content.strip():
            raise ValueError("session_id, agent_id, and content must be non-empty")
        self._purge(session_id)
        now = self._now().isoformat()
        event = SensorEvent(
            event_id=str(uuid.uuid4()), session_id=session_id, agent_id=agent_id,
            role=role.strip().lower(), content=content.strip(), created_at=now,
            source_ref=source_ref or f"session:{session_id}",
            content_hash=sha256(content.strip().encode("utf-8")).hexdigest(),
        )
        events = self._events.setdefault(session_id, [])
        events.append(event)
        if len(events) > self.max_events_per_session:
            del events[:-self.max_events_per_session]
        return event

    def snapshot(self, session_id: str, limit: Optional[int] = None) -> List[dict]:
        self._purge(session_id)
        events = self._events.get(session_id, [])
        if limit is not None:
            events = events[-limit:]
        return [asdict(item) for item in events]

    def clear(self, session_id: str) -> None:
        self._events.pop(session_id, None)

    def status(self) -> dict:
        for session_id in list(self._events):
            self._purge(session_id)
        return {
            "sessions": len(self._events),
            "events": sum(len(events) for events in self._events.values()),
            "max_events_per_session": self.max_events_per_session,
            "ttl_minutes": int(self.ttl.total_seconds() // 60),
        }
