from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
import math
from typing import Iterable

from .historical_paper_replay import HistoricalMarketBundle, HistoricalTapePoint
from .market_snapshot import _parse_datetime
from .prediction_ledger import PredictionRecord

MODEL_HISTORICAL_EVALUATOR_SCHEMA_VERSION = "polymarket-model-historical-evaluator.v2"

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
        cutoff = _parse_datetime(self.prediction_cutoff, field_name="prediction_cutoff")
        observed = _parse_datetime(self.selected_observed_at, field_name="selected_observed_at")
        known = _parse_datetime(self.selected_known_as_of, field_name="selected_known_as_of")
        acquired = _parse_datetime(self.selected_acquired_at, field_name="selected_acquired_at")
        if observed > known or known > acquired:
            raise ValueError("invalid temporal provenance ordering")
        if observed > cutoff or known > cutoff or acquired > cutoff:
            raise ValueError("selected market data is after prediction cutoff")
        if not 0.0 <= self.market_price <= 1.0 or not 0.0 <= self.model_probability <= 1.0:
            raise ValueError("probabilities must be between 0 and 1")
        if not math.isclose(self.edge, self.model_probability - self.market_price, rel_tol=0.0, abs_tol=1e-15):
            raise ValueError("edge mismatch")
        expected = 1.0 / self.market_price if self.settlement_win else 0.0
        if not math.isclose(self.settlement_units_per_notional, expected, rel_tol=0.0, abs_tol=1e-15):
            raise ValueError("settlement units mismatch")


def _time(value: str) -> datetime:
    return datetime.fromisoformat(value.replace("Z", "+00:00")).astimezone(timezone.utc)


def select_eligible_tape_point(prediction: PredictionRecord, bundle: HistoricalMarketBundle) -> HistoricalTapePoint:
    prediction.validate()
    bundle.validate()
    if prediction.market_id != bundle.market_id:
        raise ValueError("prediction market_id does not match historical bundle")
    cutoff = _parse_datetime(prediction.known_as_of, field_name="prediction.known_as_of")
    eligible = []
    for point in bundle.price_history:
        point.validate()
        if point.outcome_id != prediction.outcome_id or point.known_as_of is None or point.acquired_at is None:
            continue
        if _time(point.observed_at) <= cutoff and _time(point.known_as_of) <= cutoff and _time(point.acquired_at) <= cutoff:
            eligible.append(point)
    if not eligible:
        raise ValueError("no eligible market observation with explicit temporal provenance")
    return max(eligible, key=lambda p: (_time(p.observed_at), _time(p.known_as_of or p.observed_at), _time(p.acquired_at or p.observed_at)))


def evaluate_prediction(prediction: PredictionRecord, bundle: HistoricalMarketBundle) -> ModelHistoricalEvaluation:
    point = select_eligible_tape_point(prediction, bundle)
    if point.known_as_of is None or point.acquired_at is None:
        raise ValueError("selected market observation lacks explicit temporal provenance")
    cutoff = _parse_datetime(prediction.known_as_of, field_name="prediction.known_as_of")
    if _parse_datetime(bundle.resolution_known_at, field_name="resolution_known_at") <= cutoff:
        raise ValueError("resolution was known at or before prediction cutoff")
    price = float(point.price)
    win = prediction.outcome_id in bundle.resolution_outcome_ids
    result = ModelHistoricalEvaluation(
        MODEL_HISTORICAL_EVALUATOR_SCHEMA_VERSION,
        prediction.prediction_id,
        prediction.market_id,
        prediction.outcome_id,
        prediction.known_as_of,
        point.observed_at,
        point.known_as_of,
        point.acquired_at,
        price,
        float(prediction.probability),
        float(prediction.probability) - price,
        bundle.resolution_outcome_ids[0],
        win,
        (1.0 / price if win else 0.0),
    )
    result.validate()
    return result


def evaluate_predictions(predictions: Iterable[PredictionRecord], bundles: Iterable[HistoricalMarketBundle]) -> tuple[ModelHistoricalEvaluation, ...]:
    by_market = {bundle.market_id: bundle for bundle in bundles}
    return tuple(sorted((evaluate_prediction(p, by_market[p.market_id]) for p in predictions), key=lambda x: x.prediction_id))

__all__ = ["MODEL_HISTORICAL_EVALUATOR_SCHEMA_VERSION", "ModelHistoricalEvaluation", "select_eligible_tape_point", "evaluate_prediction", "evaluate_predictions"]
