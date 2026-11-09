"""Leakage-safe comparison of model probabilities with historical market prices."""
from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
import math
from typing import Iterable

from .historical_replay import HistoricalPricePoint
from .market_snapshot import _parse_datetime
from .prediction_ledger import PredictionRecord

MARKET_MODEL_EDGE_SCHEMA_VERSION = "polymarket-market-model-edge.v1"


@dataclass(frozen=True)
class MarketModelEdgeObservation:
    edge_id: str
    schema_version: str
    prediction_id: str
    market_id: str
    outcome_id: str
    model_probability: float
    market_price: float
    edge: float
    prediction_cutoff: str
    market_observed_at: str
    market_known_as_of: str
    market_source_type: str
    market_source_ref: str

    def canonical_payload(self) -> dict[str, object]:
        return {
            "schema_version": self.schema_version,
            "prediction_id": self.prediction_id,
            "market_id": self.market_id,
            "outcome_id": self.outcome_id,
            "model_probability": self.model_probability,
            "market_price": self.market_price,
            "edge": self.edge,
            "prediction_cutoff": self.prediction_cutoff,
            "market_observed_at": self.market_observed_at,
            "market_known_as_of": self.market_known_as_of,
            "market_source_type": self.market_source_type,
            "market_source_ref": self.market_source_ref,
        }

    def validate(self) -> None:
        if self.schema_version != MARKET_MODEL_EDGE_SCHEMA_VERSION:
            raise ValueError("unsupported market-model edge schema version")
        if not self.edge_id or not self.prediction_id or not self.market_id or not self.outcome_id:
            raise ValueError("edge identifiers are required")
        for name, value in (
            ("model_probability", self.model_probability),
            ("market_price", self.market_price),
            ("edge", self.edge),
        ):
            if not math.isfinite(value):
                raise ValueError(f"{name} must be finite")
        if not 0.0 <= self.model_probability <= 1.0:
            raise ValueError("model_probability must be between 0 and 1")
        if not 0.0 <= self.market_price <= 1.0:
            raise ValueError("market_price must be between 0 and 1")
        expected_edge = self.model_probability - self.market_price
        if not math.isclose(self.edge, expected_edge, rel_tol=0.0, abs_tol=1e-15):
            raise ValueError("edge does not equal model_probability minus market_price")
        cutoff = _parse_datetime(self.prediction_cutoff, field_name="prediction_cutoff")
        observed = _parse_datetime(self.market_observed_at, field_name="market_observed_at")
        known = _parse_datetime(self.market_known_as_of, field_name="market_known_as_of")
        if observed > known:
            raise ValueError("market observation cannot be observed after it is known")
        if known > cutoff:
            raise ValueError("market price was known after the prediction cutoff")
        if observed > cutoff:
            raise ValueError("market observation is after the prediction cutoff")
        if not self.market_source_type or not self.market_source_ref:
            raise ValueError("market source provenance is required")


@dataclass(frozen=True)
class MarketModelEdgeSummary:
    schema_version: str
    observation_count: int
    mean_edge: float
    mean_absolute_edge: float
    positive_edge_count: int
    negative_edge_count: int
    zero_edge_count: int
    observations: tuple[MarketModelEdgeObservation, ...]

    def validate(self) -> None:
        if self.schema_version != MARKET_MODEL_EDGE_SCHEMA_VERSION:
            raise ValueError("unsupported market-model edge schema version")
        if self.observation_count != len(self.observations):
            raise ValueError("observation_count must match observations")
        if self.observation_count <= 0:
            raise ValueError("at least one edge observation is required")
        for value_name, value in (
            ("mean_edge", self.mean_edge),
            ("mean_absolute_edge", self.mean_absolute_edge),
        ):
            if not math.isfinite(value):
                raise ValueError(f"{value_name} must be finite")
            if value_name == "mean_absolute_edge" and value < 0.0:
                raise ValueError("mean_absolute_edge must be non-negative")
        for observation in self.observations:
            observation.validate()
        ids = [item.prediction_id for item in self.observations]
        if len(ids) != len(set(ids)):
            raise ValueError("summary observations must have unique prediction IDs")
        if self.positive_edge_count + self.negative_edge_count + self.zero_edge_count != self.observation_count:
            raise ValueError("edge sign counts must sum to observation_count")

    def as_dict(self) -> dict[str, object]:
        self.validate()
        return {
            "schema_version": self.schema_version,
            "observation_count": self.observation_count,
            "mean_edge": self.mean_edge,
            "mean_absolute_edge": self.mean_absolute_edge,
            "positive_edge_count": self.positive_edge_count,
            "negative_edge_count": self.negative_edge_count,
            "zero_edge_count": self.zero_edge_count,
            "observations": [
                {
                    "edge_id": item.edge_id,
                    **item.canonical_payload(),
                }
                for item in self.observations
            ],
        }


def _price_as_float(point: HistoricalPricePoint) -> float:
    try:
        price = float(point.price)
    except (TypeError, ValueError) as exc:
        raise ValueError("historical market price must be numeric") from exc
    if not math.isfinite(price) or not 0.0 <= price <= 1.0:
        raise ValueError("historical market price must be finite and between 0 and 1")
    return price


def _edge_id(prediction: PredictionRecord, point: HistoricalPricePoint, model_probability: float, market_price: float) -> str:
    import hashlib
    import json

    payload = {
        "schema_version": MARKET_MODEL_EDGE_SCHEMA_VERSION,
        "prediction_id": prediction.prediction_id,
        "market_id": prediction.market_id,
        "outcome_id": prediction.outcome_id,
        "model_probability": model_probability,
        "market_price": market_price,
        "prediction_cutoff": prediction.known_as_of,
        "market_observed_at": point.observed_at,
        "market_known_as_of": point.known_as_of,
        "market_source_type": point.source_type,
        "market_source_ref": point.source_ref,
    }
    canonical = json.dumps(payload, sort_keys=True, separators=(",", ":"), ensure_ascii=True)
    return f"EDGE-{hashlib.sha256(canonical.encode('utf-8')).hexdigest()[:24]}"


def build_edge_observation(
    prediction: PredictionRecord,
    price_point: HistoricalPricePoint,
) -> MarketModelEdgeObservation:
    """Compare a prediction with one historical price point without future leakage."""
    prediction.validate()
    price_point.validate()
    if prediction.outcome_id != price_point.outcome_id:
        raise ValueError("prediction and market price outcome_id must match")
    cutoff = _parse_datetime(prediction.known_as_of, field_name="prediction.known_as_of")
    known = _parse_datetime(price_point.known_as_of, field_name="price.known_as_of")
    observed = _parse_datetime(price_point.observed_at, field_name="price.observed_at")
    if known > cutoff:
        raise ValueError("market price was known after the prediction cutoff")
    if observed > cutoff:
        raise ValueError("market observation is after the prediction cutoff")
    model_probability = float(prediction.probability)
    market_price = _price_as_float(price_point)
    edge = model_probability - market_price
    observation = MarketModelEdgeObservation(
        edge_id=_edge_id(prediction, price_point, model_probability, market_price),
        schema_version=MARKET_MODEL_EDGE_SCHEMA_VERSION,
        prediction_id=prediction.prediction_id,
        market_id=prediction.market_id,
        outcome_id=prediction.outcome_id,
        model_probability=model_probability,
        market_price=market_price,
        edge=edge,
        prediction_cutoff=prediction.known_as_of,
        market_observed_at=price_point.observed_at,
        market_known_as_of=price_point.known_as_of,
        market_source_type=price_point.source_type,
        market_source_ref=price_point.source_ref,
    )
    observation.validate()
    return observation


def select_latest_eligible_price(
    prediction: PredictionRecord,
    price_points: Iterable[HistoricalPricePoint],
) -> HistoricalPricePoint:
    """Select the latest known price at or before the prediction information cutoff.

    No interpolation, forward filling, or use of post-cutoff data is allowed.
    Ties at the same observation timestamp are rejected unless their full records agree.
    """
    prediction.validate()
    cutoff = _parse_datetime(prediction.known_as_of, field_name="prediction.known_as_of")
    eligible = []
    by_timestamp: dict[str, HistoricalPricePoint] = {}
    for point in price_points:
        point.validate()
        if point.outcome_id != prediction.outcome_id:
            continue
        observed = _parse_datetime(point.observed_at, field_name="price.observed_at")
        known = _parse_datetime(point.known_as_of, field_name="price.known_as_of")
        if observed > cutoff or known > cutoff:
            continue
        existing = by_timestamp.get(point.observed_at)
        if existing is not None and existing != point:
            raise ValueError(f"contradictory historical market prices at {point.observed_at}")
        by_timestamp[point.observed_at] = point
        eligible.append(point)
    if not eligible:
        raise ValueError("no market price was known at or before the prediction cutoff")
    return max(
        eligible,
        key=lambda point: (
            _parse_datetime(point.observed_at, field_name="price.observed_at"),
            _parse_datetime(point.known_as_of, field_name="price.known_as_of"),
            _parse_datetime(point.acquired_at, field_name="price.acquired_at"),
            point.source_ref,
        ),
    )


def compare_predictions_to_market(
    predictions: Iterable[PredictionRecord],
    price_points: Iterable[HistoricalPricePoint],
) -> MarketModelEdgeSummary:
    """Align each prediction to its latest eligible historical market price and summarize raw edge."""
    prediction_items = tuple(predictions)
    all_price_points = tuple(price_points)
    if not prediction_items:
        raise ValueError("at least one prediction is required")
    observations = []
    for prediction in prediction_items:
        point = select_latest_eligible_price(prediction, all_price_points)
        observations.append(build_edge_observation(prediction, point))
    observations = tuple(sorted(observations, key=lambda item: item.prediction_id))
    mean_edge = sum(item.edge for item in observations) / len(observations)
    mean_absolute_edge = sum(abs(item.edge) for item in observations) / len(observations)
    positive = sum(1 for item in observations if item.edge > 0.0)
    negative = sum(1 for item in observations if item.edge < 0.0)
    zero = len(observations) - positive - negative
    summary = MarketModelEdgeSummary(
        schema_version=MARKET_MODEL_EDGE_SCHEMA_VERSION,
        observation_count=len(observations),
        mean_edge=mean_edge,
        mean_absolute_edge=mean_absolute_edge,
        positive_edge_count=positive,
        negative_edge_count=negative,
        zero_edge_count=zero,
        observations=observations,
    )
    summary.validate()
    return summary


__all__ = [
    "MARKET_MODEL_EDGE_SCHEMA_VERSION",
    "MarketModelEdgeObservation",
    "MarketModelEdgeSummary",
    "build_edge_observation",
    "compare_predictions_to_market",
    "select_latest_eligible_price",
]
