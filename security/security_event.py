"""Machine-readable, non-secret security events for forensic consumers.

The event contract is intentionally metadata-only. Raw prompts, credentials,
cookies, tokens and arbitrary payloads are never serialized.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime, timezone
from typing import Any


_ALLOWED_METADATA = frozenset({
    "source_url",
    "repository",
    "path",
    "commit",
    "verification_type",
    "confidence",
    "evidence_ref",
})


def _utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


@dataclass(frozen=True)
class SecurityEvent:
    event_type: str
    source: str
    actor: str
    correlation_id: str
    timestamp: str | None = None
    trust_state: str | None = None
    scanner_verdict: str | None = None
    tool_name: str | None = None
    tool_allowed: bool | None = None
    decision_reason: str | None = None
    artifact_sha256: str | None = None
    metadata: dict[str, Any] = field(default_factory=dict)

    def to_dict(self) -> dict[str, Any]:
        metadata = {
            key: value
            for key, value in self.metadata.items()
            if key in _ALLOWED_METADATA
        }
        data: dict[str, Any] = {
            "schema_version": 1,
            "timestamp": self.timestamp or _utc_now(),
            "event_type": self.event_type,
            "source": self.source,
            "actor": self.actor,
            "correlation_id": self.correlation_id,
        }
        if self.trust_state is not None:
            data["trust_state"] = self.trust_state
        if self.scanner_verdict is not None:
            data["scanner_verdict"] = self.scanner_verdict
        if self.tool_name is not None or self.tool_allowed is not None or self.decision_reason is not None:
            data["tool"] = {
                "name": self.tool_name,
                "allowed": self.tool_allowed,
                "decision_reason": self.decision_reason,
            }
        if self.artifact_sha256 is not None:
            data["artifact"] = {"sha256": self.artifact_sha256}
        data["metadata"] = metadata
        return data
