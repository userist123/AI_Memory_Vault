"""Tamper-evident, metadata-only audit trail for security-sensitive agent activity.

The trail records what happened without storing prompts, credentials, raw tool
parameters, memory payloads, or exception messages. Each record commits the
previous record hash, making modification or deletion detectable.
"""
from __future__ import annotations

import hashlib
import json
import threading
import uuid
from dataclasses import dataclass, field
from datetime import datetime, timezone
from typing import Any


def _utc(value: datetime | None) -> str:
    current = value or datetime.now(timezone.utc)
    if current.tzinfo is None:
        current = current.replace(tzinfo=timezone.utc)
    return current.astimezone(timezone.utc).isoformat().replace("+00:00", "Z")


def _sha256(value: Any) -> str:
    if isinstance(value, str):
        raw = value.encode("utf-8")
    else:
        try:
            raw = json.dumps(
                value,
                sort_keys=True,
                separators=(",", ":"),
                ensure_ascii=True,
            ).encode("utf-8")
        except (TypeError, ValueError):
            raw = repr(value).encode("utf-8")
    return hashlib.sha256(raw).hexdigest()


@dataclass(frozen=True)
class AuditRecord:
    event_type: str
    actor: str
    correlation_id: str
    outcome: str
    timestamp: str
    event_id: str
    previous_sha256: str | None
    sha256: str
    trust_state: str | None = None
    tool_name: str | None = None
    target_sha256: str | None = None
    parameters_sha256: str | None = None
    payload_sha256: str | None = None
    approval_id: str | None = None
    decision_reason: str | None = None
    scanner_verdict: str | None = None
    error_type: str | None = None
    metadata: dict[str, str] = field(default_factory=dict)

    def unsigned_dict(self) -> dict[str, Any]:
        data = {
            "schema_version": 1,
            "event_type": self.event_type,
            "actor": self.actor,
            "correlation_id": self.correlation_id,
            "outcome": self.outcome,
            "timestamp": self.timestamp,
            "event_id": self.event_id,
            "previous_sha256": self.previous_sha256,
        }
        optional = {
            "trust_state": self.trust_state,
            "tool_name": self.tool_name,
            "target_sha256": self.target_sha256,
            "parameters_sha256": self.parameters_sha256,
            "payload_sha256": self.payload_sha256,
            "approval_id": self.approval_id,
            "decision_reason": self.decision_reason,
            "scanner_verdict": self.scanner_verdict,
            "error_type": self.error_type,
        }
        data.update({key: value for key, value in optional.items() if value is not None})
        data["metadata"] = dict(sorted(self.metadata.items()))
        return data

    def to_dict(self) -> dict[str, Any]:
        data = self.unsigned_dict()
        data["sha256"] = self.sha256
        return data


class AuditTrail:
    """Append-only, hash-chained audit records."""

    def __init__(self) -> None:
        self._records: list[AuditRecord] = []
        self._lock = threading.RLock()

    @property
    def records(self) -> tuple[AuditRecord, ...]:
        with self._lock:
            return tuple(self._records)

    def record(
        self,
        *,
        event_type: str,
        actor: str,
        correlation_id: str,
        outcome: str,
        trust_state: str | None = None,
        tool_name: str | None = None,
        target: str | None = None,
        parameters_sha256: str | None = None,
        payload: Any = None,
        approval_id: str | None = None,
        decision_reason: str | None = None,
        scanner_verdict: str | None = None,
        error: BaseException | None = None,
        metadata: dict[str, str] | None = None,
        timestamp: datetime | None = None,
    ) -> AuditRecord:
        with self._lock:
            previous = self._records[-1].sha256 if self._records else None
            record = AuditRecord(
                event_type=event_type,
                actor=actor,
                correlation_id=correlation_id,
                outcome=outcome,
                timestamp=_utc(timestamp),
                event_id=uuid.uuid4().hex,
                previous_sha256=previous,
                sha256="",
                trust_state=trust_state,
                tool_name=tool_name,
                target_sha256=_sha256(target) if target is not None else None,
                parameters_sha256=parameters_sha256,
                payload_sha256=_sha256(payload) if payload is not None else None,
                approval_id=approval_id,
                decision_reason=decision_reason,
                scanner_verdict=scanner_verdict,
                error_type=type(error).__name__ if error is not None else None,
                metadata=dict(metadata or {}),
            )
            digest = hashlib.sha256(
                json.dumps(
                    record.unsigned_dict(),
                    sort_keys=True,
                    separators=(",", ":"),
                    ensure_ascii=True,
                ).encode("utf-8")
            ).hexdigest()
            record = AuditRecord(**{**record.__dict__, "sha256": digest})
            self._records.append(record)
            return record

    def verify(self) -> bool:
        with self._lock:
            previous = None
            for record in self._records:
                if record.previous_sha256 != previous:
                    return False
                expected = hashlib.sha256(
                    json.dumps(
                        record.unsigned_dict(),
                        sort_keys=True,
                        separators=(",", ":"),
                        ensure_ascii=True,
                    ).encode("utf-8")
                ).hexdigest()
                if record.sha256 != expected:
                    return False
                previous = record.sha256
            return True

    def export(self) -> list[dict[str, Any]]:
        with self._lock:
            return [record.to_dict() for record in self._records]
