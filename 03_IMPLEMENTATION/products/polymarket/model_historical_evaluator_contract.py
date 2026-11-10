"""Research-only historical evaluator contract with explicit temporal provenance.

This module intentionally does not mutate Phase 11 tape structures. It defines a
separate provenance-bearing point contract and evaluates only points whose
observation, knowledge, and acquisition timestamps are all at or before the
prediction cutoff. Missing provenance fails closed.
"""
from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Iterable, Sequence


SCHEMA_VERSION = "polymarket-model-historical-evaluator.contract.v1"


def _parse(value: str) -> datetime:
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    if parsed.tzinfo is None:
        raise ValueError("timestamp must include timezone")
    return parsed.astimezone(timezone.utc)


@dataclass(frozen=True)
class TemporalTapePoint:
    market_id: str
    outcome_id: str
    observed_at: str
    price: float
    known_as_of: str | None
    acquired_at: str | None

    def validate(self) -> None:
        if not self.market_id or not self.outcome_id:
            raise ValueError("market_id and outcome_id are required")
        if not 0.0 <= self.price <= 1.0:
            raise ValueError("price must be in [0, 1]")
        observed = _parse(self.observed_at)
        if self.known_as_of is None or self.acquired_at is None:
            raise ValueError("explicit known_as_of and acquired_at are required")
        known = _parse(self.known_as_of)
        acquired = _parse(self.acquired_at)
        if observed > known:
            raise ValueError("observed_at cannot be after known_as_of")
        if known > acquired:
            raise ValueError("known_as_of cannot be after acquired_at")


@dataclass(frozen=True)
class EvaluatorPrediction:
    market_id: str
    outcome_id: str
    probability: float
    known_as_of: str


@dataclass(frozen=True)
class HistoricalEvaluation:
    market_id: str
    outcome_id: str
    prediction_known_as_of: str
    selected_observed_at: str
    selected_known_as_of: str
    selected_acquired_at: str
    observed_price: float
    probability: float
    raw_edge: float
    settled_units: float


def select_eligible_point(
    prediction: EvaluatorPrediction,
    points: Sequence[TemporalTapePoint],
) -> TemporalTapePoint:
    cutoff = _parse(prediction.known_as_of)
    eligible: list[TemporalTapePoint] = []
    for point in points:
        point.validate()
        if point.market_id != prediction.market_id or point.outcome_id != prediction.outcome_id:
            continue
        if _parse(point.observed_at) > cutoff:
            continue
        if _parse(point.known_as_of) > cutoff:
            continue
        if _parse(point.acquired_at) > cutoff:
            continue
        eligible.append(point)
    if not eligible:
        raise ValueError("no temporally eligible historical point")
    return max(eligible, key=lambda item: _parse(item.observed_at))


def evaluate_prediction(
    prediction: EvaluatorPrediction,
    points: Sequence[TemporalTapePoint],
    resolved_outcome_id: str,
    resolution_known_at: str,
) -> HistoricalEvaluation:
    if not 0.0 <= prediction.probability <= 1.0:
        raise ValueError("probability must be in [0, 1]")
    cutoff = _parse(prediction.known_as_of)
    if _parse(resolution_known_at) <= cutoff:
        raise ValueError("resolution cannot be known at or before prediction cutoff")
    selected = select_eligible_point(prediction, points)
    raw_edge = prediction.probability - selected.price
    settled_units = (1.0 / selected.price) if selected.outcome_id == resolved_outcome_id else 0.0
    return HistoricalEvaluation(
        market_id=prediction.market_id,
        outcome_id=prediction.outcome_id,
        prediction_known_as_of=prediction.known_as_of,
        selected_observed_at=selected.observed_at,
        selected_known_as_of=selected.known_as_of or "",
        selected_acquired_at=selected.acquired_at or "",
        observed_price=selected.price,
        probability=prediction.probability,
        raw_edge=raw_edge,
        settled_units=settled_units,
    )


def evaluate_predictions(
    predictions: Iterable[EvaluatorPrediction],
    points: Sequence[TemporalTapePoint],
    resolved_outcome_id: str,
    resolution_known_at: str,
) -> list[HistoricalEvaluation]:
    return [
        evaluate_prediction(prediction, points, resolved_outcome_id, resolution_known_at)
        for prediction in predictions
    ]
