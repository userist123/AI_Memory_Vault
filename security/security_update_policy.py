"""Mandatory security-update policy and verification primitives."""
from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass
from datetime import datetime, timezone
from enum import Enum
from typing import Callable


class UpdateSeverity(str, Enum):
    LOW = "LOW"
    MODERATE = "MODERATE"
    HIGH = "HIGH"
    CRITICAL = "CRITICAL"


class SecurityUpdateRequired(RuntimeError):
    pass


@dataclass(frozen=True)
class SecurityUpdate:
    update_id: str
    version: str
    severity: UpdateSeverity
    released_at: datetime
    mandatory_after: datetime | None
    min_runtime_version: str
    package_sha256: str
    notes: str = ""

    def canonical_bytes(self) -> bytes:
        payload = {
            "update_id": self.update_id,
            "version": self.version,
            "severity": self.severity.value,
            "released_at": self.released_at.astimezone(timezone.utc).isoformat(),
            "mandatory_after": (
                self.mandatory_after.astimezone(timezone.utc).isoformat()
                if self.mandatory_after else None
            ),
            "min_runtime_version": self.min_runtime_version,
            "package_sha256": self.package_sha256,
            "notes": self.notes,
        }
        return json.dumps(payload, sort_keys=True, separators=(",", ":")).encode()


@dataclass(frozen=True)
class SecurityUpdateState:
    installed_version: str
    required_min_version: str
    blocked: bool
    reason: str | None = None


def _version(value: str) -> tuple[int, ...]:
    parts = value.split(".")
    if not parts or any(not part.isdigit() for part in parts):
        raise ValueError(f"invalid security version: {value}")
    return tuple(int(part) for part in parts)


class SecurityUpdatePolicy:
    """Fail-closed policy for mandatory security updates."""

    def __init__(
        self,
        current_version: str,
        *,
        clock: Callable[[], datetime] | None = None,
    ) -> None:
        _version(current_version)
        self.current_version = current_version
        self._clock = clock or (lambda: datetime.now(timezone.utc))
        self._required_min_version = current_version
        self._active_update: SecurityUpdate | None = None

    def apply_manifest(self, update: SecurityUpdate) -> SecurityUpdateState:
        if _version(update.version) <= _version(self.current_version):
            return self.state()

        if _version(update.min_runtime_version) > _version(self._required_min_version):
            self._required_min_version = update.min_runtime_version

        self._active_update = update
        return self.state()

    def state(self) -> SecurityUpdateState:
        now = self._clock().astimezone(timezone.utc)
        required = max(
            _version(self._required_min_version),
            _version(self._active_update.version) if self._active_update else _version(self.current_version),
        )
        blocked = _version(self.current_version) < required

        if self._active_update and self._active_update.mandatory_after:
            blocked = blocked and now >= self._active_update.mandatory_after

        reason = (
            f"mandatory security update required: {self._active_update.update_id}"
            if blocked and self._active_update else None
        )
        return SecurityUpdateState(
            installed_version=self.current_version,
            required_min_version=".".join(map(str, required)),
            blocked=blocked,
            reason=reason,
        )

    def enforce(self, *, protected_operation: bool = True) -> None:
        state = self.state()
        if protected_operation and state.blocked:
            raise SecurityUpdateRequired(state.reason or "security update required")


def verify_package_sha256(package_bytes: bytes, expected_sha256: str) -> bool:
    actual = hashlib.sha256(package_bytes).hexdigest()
    return actual.lower() == expected_sha256.lower()
