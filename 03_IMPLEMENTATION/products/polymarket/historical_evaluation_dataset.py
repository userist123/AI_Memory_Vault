"""Fail-closed dataset boundary for historical Polymarket model evaluation."""
from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Any, Iterable, Mapping

from .model_historical_evaluator_contract import EvaluatorPrediction, HistoricalEvaluation, TemporalTapePoint, evaluate_prediction
from .temporal_provenance_contract import ProvenanceRecord

SCHEMA_VERSION = "polymarket-historical-evaluation-dataset.v1"
_SAFE_BASES = {"source_native", "event_derived"}


def _parse(value: str) -> datetime:
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    if parsed.tzinfo is None:
        raise ValueError("timestamp must include timezone")
    return parsed.astimezone(timezone.utc)


def _text(record: Mapping[str, Any], field: str) -> str:
    value = record.get(field)
    if not isinstance(value, str) or not value:
        raise ValueError(f"{field} is required")
    return value


def _number(record: Mapping[str, Any], field: str) -> float:
    value = record.get(field)
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ValueError(f"{field} must be numeric")
    return float(value)


@dataclass(frozen=True)
class HistoricalEvaluationDatasetRow:
    prediction: EvaluatorPrediction
    point: TemporalTapePoint
    provenance: ProvenanceRecord
    resolved_outcome_id: str
    resolution_known_at: str
    source_timestamp_semantics: str

    def validate(self) -> None:
        self.provenance.validate()
        if self.provenance.knowledge_basis not in _SAFE_BASES:
            raise ValueError("dataset row is not cutoff-safe historical provenance")
        cutoff = _parse(self.prediction.known_as_of)
        observed = _parse(self.provenance.observed_at)
        known = _parse(self.provenance.known_as_of)
        acquired = _parse(self.provenance.acquired_at)
        resolution = _parse(self.resolution_known_at)
        if observed > cutoff or known > cutoff or acquired > cutoff:
            raise ValueError("dataset point is not temporally eligible at prediction cutoff")
        if resolution <= cutoff:
            raise ValueError("resolution cannot be known at or before prediction cutoff")
        if self.point.known_as_of != self.provenance.known_as_of or self.point.acquired_at != self.provenance.acquired_at:
            raise ValueError("temporal tape point provenance mismatch")
        if self.point.observed_at != self.provenance.observed_at:
            raise ValueError("observed_at mismatch")
        if not self.source_timestamp_semantics:
            raise ValueError("source_timestamp_semantics is required")
        if not self.resolved_outcome_id:
            raise ValueError("resolved_outcome_id is required")
        if not 0.0 < self.point.price < 1.0:
            raise ValueError("historical evaluation price must be strictly between 0 and 1")
        self.point.validate()

    def evaluate(self) -> HistoricalEvaluation:
        self.validate()
        return evaluate_prediction(
            self.prediction,
            [self.point],
            self.resolved_outcome_id,
            self.resolution_known_at,
        )


def row_from_mapping(record: Mapping[str, Any]) -> HistoricalEvaluationDatasetRow:
    observed_at = _text(record, "observed_at")
    known_as_of = _text(record, "known_as_of")
    acquired_at = _text(record, "acquired_at")
    source_ref = _text(record, "source_ref")
    knowledge_basis = _text(record, "knowledge_basis")
    source_timestamp_semantics = _text(record, "source_timestamp_semantics")
    prediction_cutoff = _text(record, "prediction_known_as_of")
    resolution_known_at = _text(record, "resolution_known_at")

    provenance = ProvenanceRecord(
        observed_at=observed_at,
        known_as_of=known_as_of,
        acquired_at=acquired_at,
        source_ref=source_ref,
        knowledge_basis=knowledge_basis,
    )
    provenance.validate()
    if knowledge_basis not in _SAFE_BASES:
        raise ValueError("dataset requires source_native or event_derived provenance")

    prediction = EvaluatorPrediction(
        market_id=_text(record, "market_id"),
        outcome_id=_text(record, "outcome_id"),
        probability=_number(record, "probability"),
        known_as_of=prediction_cutoff,
    )
    point = TemporalTapePoint(
        market_id=prediction.market_id,
        outcome_id=prediction.outcome_id,
        observed_at=observed_at,
        price=_number(record, "price"),
        known_as_of=known_as_of,
        acquired_at=acquired_at,
    )
    row = HistoricalEvaluationDatasetRow(
        prediction=prediction,
        point=point,
        provenance=provenance,
        resolved_outcome_id=_text(record, "resolved_outcome_id"),
        resolution_known_at=resolution_known_at,
        source_timestamp_semantics=source_timestamp_semantics,
    )
    row.validate()
    return row


def load_historical_evaluation_dataset(records: Iterable[Mapping[str, Any]]) -> tuple[HistoricalEvaluationDatasetRow, ...]:
    rows = tuple(row_from_mapping(record) for record in records)
    if not rows:
        raise ValueError("historical evaluation dataset is empty")
    return rows


__all__ = [
    "HistoricalEvaluationDatasetRow",
    "load_historical_evaluation_dataset",
    "row_from_mapping",
    "SCHEMA_VERSION",
]
