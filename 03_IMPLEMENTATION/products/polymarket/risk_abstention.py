"""Deterministic analysis-only market decision gating and abstention."""
from __future__ import annotations

from dataclasses import dataclass
import math
from typing import Optional

from .historical_replay import HistoricalPricePoint
from .market_model_edge import select_latest_eligible_price
from .prediction_ledger import PredictionRecord

RISK_ABSTENTION_SCHEMA_VERSION = "polymarket-risk-abstention.v1"
DECISION_BET = "BET"
DECISION_ABSTAIN = "ABSTAIN"


@dataclass(frozen=True)
class AbstentionPolicy:
    """Pure analysis policy; it never creates orders or execution instructions."""

    min_positive_edge: float = 0.05

    def validate(self) -> None:
        if not math.isfinite(self.min_positive_edge):
            raise ValueError("min_positive_edge must be finite")
        if not 0.0 <= self.min_positive_edge <= 1.0:
            raise ValueError("min_positive_edge must be between 0 and 1")


@dataclass(frozen=True)
class AbstentionDecision:
    schema_version: str
    prediction_id: str
    market_id: str
    outcome_id: str
    market_price: Optional[float]
    model_probability: float
    edge: Optional[float]
    decision: str
    reason: str

    def validate(self) -> None:
        if self.schema_version != RISK_ABSTENTION_SCHEMA_VERSION:
            raise ValueError("unsupported risk-abstention schema version")
        if self.decision not in (DECISION_BET, DECISION_ABSTAIN):
            raise ValueError("decision must be BET or ABSTAIN")
        if not self.prediction_id or not self.market_id or not self.outcome_id:
            raise ValueError("prediction and market identifiers are required")
        if not math.isfinite(self.model_probability) or not 0.0 <= self.model_probability <= 1.0:
            raise ValueError("model_probability must be between 0 and 1")
        if self.market_price is not None and (not math.isfinite(self.market_price) or not 0.0 <= self.market_price <= 1.0):
            raise ValueError("market_price must be between 0 and 1")
        if self.edge is not None and not math.isfinite(self.edge):
            raise ValueError("edge must be finite")

    def as_dict(self) -> dict[str, object]:
        self.validate()
        return {
            "schema_version": self.schema_version,
            "prediction_id": self.prediction_id,
            "market_id": self.market_id,
            "outcome_id": self.outcome_id,
            "market_price": self.market_price,
            "model_probability": self.model_probability,
            "edge": self.edge,
            "decision": self.decision,
            "reason": self.reason,
        }


def evaluate_prediction(
    prediction: PredictionRecord,
    *,
    market_price: Optional[float],
    policy: AbstentionPolicy = AbstentionPolicy(),
) -> AbstentionDecision:
    """Apply a deterministic BET/ABSTAIN gate; no bankroll, sizing, EV, or execution."""
    prediction.validate()
    policy.validate()

    if prediction.abstained:
        decision = AbstentionDecision(
            RISK_ABSTENTION_SCHEMA_VERSION,
            prediction.prediction_id,
            prediction.market_id,
            prediction.outcome_id,
            market_price,
            prediction.probability,
            None if market_price is None else prediction.probability - market_price,
            DECISION_ABSTAIN,
            "prediction_already_abstained",
        )
        decision.validate()
        return decision

    if market_price is None:
        decision = AbstentionDecision(
            RISK_ABSTENTION_SCHEMA_VERSION,
            prediction.prediction_id,
            prediction.market_id,
            prediction.outcome_id,
            None,
            prediction.probability,
            None,
            DECISION_ABSTAIN,
            "missing_historical_market_price",
        )
        decision.validate()
        return decision

    if not math.isfinite(market_price) or not 0.0 <= market_price <= 1.0:
        raise ValueError("market_price must be finite and between 0 and 1")

    edge = prediction.probability - market_price

    #: An edge exactly at the threshold must abstain, and binary floating point
    #: does not let `<=` say so. 0.55 - 0.50 is 0.050000000000000044, which is
    #: greater than 0.05 by 4.2e-17, so a prediction sitting precisely on a
    #: 5-point threshold was returning BET. The gate failed open, which is the
    #: wrong direction for the one component whose purpose is to decline.
    #:
    #: Tolerances are absolute as well as relative because the quantity is a
    #: probability difference: rel_tol alone is meaningless as the threshold
    #: approaches zero, which is exactly where a "bet on any positive edge"
    #: policy would sit.
    at_threshold = math.isclose(
        edge, policy.min_positive_edge, rel_tol=1e-9, abs_tol=1e-12
    )
    if at_threshold:
        reason = "edge_at_abstention_threshold"
        decision_name = DECISION_ABSTAIN
    elif edge < policy.min_positive_edge:
        reason = "edge_below_abstention_threshold"
        decision_name = DECISION_ABSTAIN
    else:
        reason = "positive_edge_above_threshold"
        decision_name = DECISION_BET

    decision = AbstentionDecision(
        RISK_ABSTENTION_SCHEMA_VERSION,
        prediction.prediction_id,
        prediction.market_id,
        prediction.outcome_id,
        market_price,
        prediction.probability,
        edge,
        decision_name,
        reason,
    )
    decision.validate()
    return decision


def evaluate_prediction_against_history(
    prediction: PredictionRecord,
    price_points: list[HistoricalPricePoint] | tuple[HistoricalPricePoint, ...],
    *,
    policy: AbstentionPolicy = AbstentionPolicy(),
) -> AbstentionDecision:
    """Resolve the latest eligible historical price, then apply the analysis-only gate."""
    point = select_latest_eligible_price(prediction, price_points)
    return evaluate_prediction(prediction, market_price=None if point is None else float(point.price), policy=policy)


__all__ = [
    "AbstentionPolicy",
    "AbstentionDecision",
    "DECISION_ABSTAIN",
    "DECISION_BET",
    "RISK_ABSTENTION_SCHEMA_VERSION",
    "evaluate_prediction",
    "evaluate_prediction_against_history",
]
