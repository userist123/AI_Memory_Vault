"""Append-only prediction ledger with immutable provenance bindings."""
from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
import hashlib
import json
import math
from typing import Any, Iterable, Mapping, Optional

from .market_snapshot import _parse_datetime

PREDICTION_LEDGER_SCHEMA_VERSION = "polymarket-prediction-ledger.v1"


def _canonical(value: Any) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True)


def _hash(value: Any) -> str:
    return hashlib.sha256(_canonical(value).encode("utf-8")).hexdigest()


@dataclass(frozen=True)
class PredictionProvenance:
    evidence_bundle_hash: str
    snapshot_ids: tuple[str, ...]
    source_refs: tuple[str, ...]
    source_types: tuple[str, ...]
    model_id: str
    model_version: str
    prompt_hash: Optional[str] = None

    def validate(self) -> None:
        if not self.evidence_bundle_hash:
            raise ValueError("evidence_bundle_hash is required")
        if not self.snapshot_ids:
            raise ValueError("at least one snapshot_id is required")
        if len(set(self.snapshot_ids)) != len(self.snapshot_ids):
            raise ValueError("snapshot_ids must be unique")
        if not self.source_refs or not all(self.source_refs):
            raise ValueError("source_refs are required")
        if not self.source_types or not all(self.source_types):
            raise ValueError("source_types are required")
        if len(self.source_refs) != len(self.source_types):
            raise ValueError("source_refs and source_types must align")
        if not self.model_id or not self.model_version:
            raise ValueError("model_id and model_version are required")

    def as_dict(self) -> dict[str, Any]:
        self.validate()
        return {
            "evidence_bundle_hash": self.evidence_bundle_hash,
            "snapshot_ids": list(self.snapshot_ids),
            "source_refs": list(self.source_refs),
            "source_types": list(self.source_types),
            "model_id": self.model_id,
            "model_version": self.model_version,
            "prompt_hash": self.prompt_hash,
        }


@dataclass(frozen=True)
class PredictionRecord:
    prediction_id: str
    schema_version: str
    market_id: str
    outcome_id: str
    probability: float
    predicted_at: str
    known_as_of: str
    provenance: PredictionProvenance
    abstained: bool = False
    note: Optional[str] = None

    def canonical_payload(self) -> dict[str, Any]:
        return {
            "schema_version": self.schema_version,
            "market_id": self.market_id,
            "outcome_id": self.outcome_id,
            "probability": self.probability,
            "predicted_at": self.predicted_at,
            "known_as_of": self.known_as_of,
            "provenance": self.provenance.as_dict(),
            "abstained": self.abstained,
            "note": self.note,
        }

    def expected_id(self) -> str:
        return f"PRED-{_hash(self.canonical_payload())[:24]}"

    def validate(self) -> None:
        if self.schema_version != PREDICTION_LEDGER_SCHEMA_VERSION:
            raise ValueError("unsupported prediction ledger schema version")
        if not self.market_id or not self.outcome_id:
            raise ValueError("market_id and outcome_id are required")
        if not math.isfinite(self.probability) or not 0.0 <= self.probability <= 1.0:
            raise ValueError("probability must be finite and between 0 and 1")
        predicted_at = _parse_datetime(self.predicted_at, field_name="predicted_at")
        known_as_of = _parse_datetime(self.known_as_of, field_name="known_as_of")
        if known_as_of > predicted_at:
            raise ValueError("known_as_of cannot be after predicted_at")
        self.provenance.validate()
        if self.prediction_id != self.expected_id():
            raise ValueError("prediction_id does not match canonical prediction content")
        if self.abstained and self.probability not in (0.0, 1.0):
            raise ValueError("abstained predictions must use an explicit boundary probability")

    def as_dict(self) -> dict[str, Any]:
        self.validate()
        return {"prediction_id": self.prediction_id, **self.canonical_payload()}


class PredictionLedger:
    """In-memory append-only ledger; records are immutable once accepted."""

    def __init__(self, records: Iterable[PredictionRecord] = ()) -> None:
        self._records: dict[str, PredictionRecord] = {}
        for record in records:
            self.append(record)

    def append(self, record: PredictionRecord) -> str:
        record.validate()
        existing = self._records.get(record.prediction_id)
        if existing is not None and existing != record:
            raise ValueError(f"prediction_id collision: {record.prediction_id}")
        self._records[record.prediction_id] = record
        return record.prediction_id

    def get(self, prediction_id: str) -> PredictionRecord:
        try:
            return self._records[prediction_id]
        except KeyError as exc:
            raise KeyError(f"unknown prediction_id: {prediction_id}") from exc

    def records(self) -> tuple[PredictionRecord, ...]:
        return tuple(self._records[key] for key in sorted(self._records))

    def export(self) -> list[dict[str, Any]]:
        return [record.as_dict() for record in self.records()]


def build_prediction(
    *,
    market_id: str,
    outcome_id: str,
    probability: float,
    predicted_at: str,
    known_as_of: str,
    provenance: PredictionProvenance,
    abstained: bool = False,
    note: Optional[str] = None,
) -> PredictionRecord:
    provisional = PredictionRecord(
        prediction_id="",
        schema_version=PREDICTION_LEDGER_SCHEMA_VERSION,
        market_id=market_id,
        outcome_id=outcome_id,
        probability=probability,
        predicted_at=predicted_at,
        known_as_of=known_as_of,
        provenance=provenance,
        abstained=abstained,
        note=note,
    )
    record = PredictionRecord(
        prediction_id=provisional.expected_id(),
        **{k: getattr(provisional, k) for k in provisional.__dataclass_fields__ if k != "prediction_id"},
    )
    record.validate()
    return record


__all__ = [
    "PREDICTION_LEDGER_SCHEMA_VERSION",
    "PredictionProvenance",
    "PredictionRecord",
    "PredictionLedger",
    "build_prediction",
]
