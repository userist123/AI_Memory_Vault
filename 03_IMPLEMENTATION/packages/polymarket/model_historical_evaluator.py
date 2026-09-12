"""Leakage-safe model-only historical evaluation; research/analysis only."""
from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
import math
from typing import Iterable

from .historical_paper_replay import HistoricalMarketBundle, HistoricalTapePoint
from .market_snapshot import _parse_datetime
from .prediction_ledger import PredictionRecord

MODEL_HISTORICAL_EVALUATOR_SCHEMA_VERSION = "polymarket-model-historical-evaluator.v1"


@dataclass(frozen=True)
class ModelHistoricalEvaluation:
    schema_version: str
    prediction_id: str
    market_id: str
    outcome_id: str
    prediction_cutoff: str
    selected_observed_at: str
    selected_known_as_of: str
    selected_acquired_at: str
    market_price: float
    model_probability: float
    edge: float
    resolution_outcome_id: str
    settlement_win: bool
    settlement_units_per_notional: float

    def validate(self) -> None:
        if self.schema_version != MODEL_HISTORICAL_EVALUATOR_SCHEMA_VERSION:
            raise ValueError("unsupported model historical evaluator schema version")
        if not self.prediction_id or not self.market_id or not self.outcome_id:
            raise ValueError("evaluation identifiers are required")
        cutoff = _parse_datetime(self.prediction_cutoff, field_name="prediction_cutoff")
        observed = _parse_datetime(self.selected_observed_at, field_name="selected_observed_at")
        known = _parse_datetime(self.selected_known_as_of, field_name="selected_known_as_of")
        acquired = _parse_datetime(self.selected_acquired_at, field_name="selected_acquired_at")
        if observed > known:
            raise ValueError("selected observation cannot be known before it was observed")
        if known > acquired:
            raise ValueError("selected market knowledge cannot postdate acquisition")
        if observed > cutoff or known > cutoff or acquired > cutoff:
            raise ValueError("selected market data is after prediction cutoff")
        if not 0.0 <= self.market_price <= 1.0:
            raise ValueError("market_price must be between 0 and 1")
        if not 0.0 <= self.model_probability <= 1.0:
            raise ValueError("model_probability must be between 0 and 1")
        if not math.isfinite(self.edge) or not math.isclose(self.edge, self.model_probability - self.market_price, rel_tol=0.0, abs_tol=1e-15):
            raise ValueError("edge mismatch")
        if not self.resolution_outcome_id:
            raise ValueError("resolution_outcome_id is required")
        if self.settlement_units_per_notional < 0.0 or not math.isfinite(self.settlement_units_per_notional):
            raise ValueError("settlement_units_per_notional must be finite and non-negative")
        expected = 1.0 / self.market_price if self.settlement_win else 0.0
        if not math.isclose(self.settlement_units_per_notional, expected, rel_tol=0.0, abs_tol=1e-15):
            raise ValueError("settlement units do not match binary settlement")


def _parse_tape_time(value: str) -> datetime:
    return datetime.fromisoformat(value.replace("Z", "+00:00")).astimezone(timezone.utc)


def select_eligible_tape_point(prediction: PredictionRecord, bundle: HistoricalMarketBundle) -> HistoricalTapePoint:
    """Select latest market-bound observation fully known at prediction cutoff."""
    prediction.validate()
    bundle.validate()
    if prediction.market_id != bundle.market_id:
        raise ValueError("prediction market_id does not match historical bundle")
    cutoff = _parse_datetime(prediction.known_as_of, field_name="prediction.known_as_of")
    candidates = []
    for point in bundle.price_history:
        point.validate()
        if point.outcome_id != prediction.outcome_id:
            continue
        observed = _parse_tape_time(point.observed_at)
        if observed <= cutoff:
            candidates.append(point)
    if not candidates:
        raise ValueError("no eligible market observation at or before prediction cutoff")
    return max(candidates, key=lambda point: _parse_tape_time(point.observed_at))


def evaluate_prediction(prediction: PredictionRecord, bundle: HistoricalMarketBundle) -> ModelHistoricalEvaluation:
    """Evaluate one prediction against real historical market data and terminal resolution."""
    point = select_eligible_tape_point(prediction, bundle)
    cutoff = _parse_datetime(prediction.known_as_of, field_name="prediction.known_as_of")
    resolution_known = _parse_datetime(bundle.resolution_known_at, field_name="resolution_known_at")
    if resolution_known <= cutoff:
        raise ValueError("resolution was known at or before prediction cutoff")
    market_price = float(point.price)
    model_probability = float(prediction.probability)
    win = prediction.outcome_id in bundle.resolution_outcome_ids
    result = ModelHistoricalEvaluation(
        schema_version=MODEL_HISTORICAL_EVALUATOR_SCHEMA_VERSION,
        prediction_id=prediction.prediction_id,
        market_id=prediction.market_id,
        outcome_id=prediction.outcome_id,
        prediction_cutoff=prediction.known_as_of,
        selected_observed_at=point.observed_at,
        selected_known_as_of=prediction.known_as_of,
        selected_acquired_at=prediction.known_as_of,
        market_price=market_price,
        model_probability=model_probability,
        edge=model_probability - market_price,
        resolution_outcome_id=bundle.resolution_outcome_ids[0],
        settlement_win=win,
        settlement_units_per_notional=(1.0 / market_price if win else 0.0),
    )
    result.validate()
    return result


def evaluate_predictions(predictions: Iterable[PredictionRecord], bundles: Iterable[HistoricalMarketBundle]) -> tuple[ModelHistoricalEvaluation, ...]:
    by_market = {bundle.market_id: bundle for bundle in bundles}
    results = []
    for prediction in predictions:
        bundle = by_market.get(prediction.market_id)
        if bundle is None:
            raise ValueError(f"missing historical bundle for market {prediction.market_id}")
        results.append(evaluate_prediction(prediction, bundle))
    return tuple(sorted(results, key=lambda item: item.prediction_id))


__all__ = [
    "MODEL_HISTORICAL_EVALUATOR_SCHEMA_VERSION",
    "ModelHistoricalEvaluation",
    "select_eligible_tape_point",
    "evaluate_prediction",
    "evaluate_predictions",
]
