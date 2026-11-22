from __future__ import annotations

import re
import uuid
from datetime import UTC, datetime

_ID_RE = re.compile(r"^[a-z][a-z0-9]*_[0-9a-f]{12,32}$")
_SLUG_RE = re.compile(r"^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$")


def new_id(prefix: str) -> str:
    return f"{prefix}_{uuid.uuid4().hex[:16]}"


def is_valid_id(value: str) -> bool:
    return bool(_ID_RE.match(value))


def is_valid_slug(value: str) -> bool:
    return bool(_SLUG_RE.match(value))


def utcnow() -> datetime:
    return datetime.now(UTC)


def iso(dt: datetime | None = None) -> str:
    dt = dt or utcnow()
    if dt.tzinfo is None:  # SQLite returns naive datetimes; they are stored as UTC
        dt = dt.replace(tzinfo=UTC)
    return dt.astimezone(UTC).isoformat(timespec="microseconds").replace("+00:00", "Z")
