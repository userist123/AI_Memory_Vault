from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone


SCHEMA_VERSION = "polymarket-temporal-provenance.v1"


def _ts(value: str) -> datetime:
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    if parsed.tzinfo is None:
        raise ValueError("timestamp must include timezone")
    return parsed.astimezone(timezone.utc)


@dataclass(frozen=True)
class ProvenanceRecord:
    observed_at: str
    known_as_of: str
    acquired_at: str
    source_ref: str
    knowledge_basis: str

    def validate(self) -> None:
        observed = _ts(self.observed_at)
        known = _ts(self.known_as_of)
        acquired = _ts(self.acquired_at)
        if not self.source_ref:
            raise ValueError("source_ref is required")
        if observed > known:
            raise ValueError("observed_at cannot be after known_as_of")
        if known > acquired:
            raise ValueError("known_as_of cannot be after acquired_at")
        if self.knowledge_basis not in {"source_native", "event_derived", "acquisition_only", "unknown"}:
            raise ValueError("invalid knowledge_basis")

    def is_cutoff_safe(self, cutoff: str) -> bool:
        self.validate()
        if self.knowledge_basis not in {"source_native", "event_derived"}:
            return False
        limit = _ts(cutoff)
        return _ts(self.observed_at) <= limit and _ts(self.known_as_of) <= limit and _ts(self.acquired_at) <= limit


@dataclass(frozen=True)
class CaptureInput:
    observed_at: str
    acquired_at: str
    source_ref: str
    knowledge_basis: str
    historical_knowledge_at: str | None = None

    def materialize(self) -> ProvenanceRecord:
        known = self.historical_knowledge_at or self.acquired_at
        record = ProvenanceRecord(
            observed_at=self.observed_at,
            known_as_of=known,
            acquired_at=self.acquired_at,
            source_ref=self.source_ref,
            knowledge_basis=self.knowledge_basis,
        )
        record.validate()
        if self.knowledge_basis in {"source_native", "event_derived"} and self.historical_knowledge_at is None:
            raise ValueError("historical_knowledge_at is required for historical knowledge basis")
        return record
