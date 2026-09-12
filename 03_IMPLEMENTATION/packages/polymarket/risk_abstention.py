"""Deterministic analysis-only risk gating, abstention, and bounded sizing diagnostics."""
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
class RiskAbstentionPolicy:
    """Pure analysis policy; values never create orders or execution instructions."""

    min_positive_edge: float = 0.05
    max_risk_fraction: float = 0.02
    edge_at_max_risk: float = 0.10

    def validate(self) -> None:
        values = (self.min_positive_edge, self.max_risk_fraction, self.edge_at_max_risk)
        if not all(math.isfinite(value) for value in values):
            raise ValueError("risk policy values must be finite")
        if not 0.0 <= self.min_positive_edge <= 1.0:
            raise ValueError("min_positive_edge must be between 0 and 1")
        if not 0.0 < self.max_risk_fraction <= 1.0:
            raise ValueError("max_risk_fraction must be in (0, 1]")
        if not self.min_positive_edge < self.edge_at_max_risk <= 1.0:
            raise ValueError("edge_at_max_risk must exceed min_positive_edge and be at most 1")


@dataclass(frozen=True)
class RiskAbstentionDecision:
    schema_version: str
    prediction_id: str
    market_id: str
    outcome_id: str
    market_price: Optional[float]
    model_probability: float
    edge: Optional[float]
    decision: str
    reason: str
    bounded_risk_fraction: float

    def validate(self) -> None:
        if self.schema_version != RISK_ABSTENTION_SCHEMA_VERSION:
            raise ValueError("unsupported risk-abstention schema version")
        if self.decision not in (DECISION_BET, DECISION_ABSTAIN):
            raise ValueError("decision must be BET or ABSTAIN")
        if not self.prediction_id or not self.market_id or not self.outcome_id:
            raise ValueError("prediction and market identifiers are required")
        if not math.isfinite(self.model_probability) or not 0.0 <= self.model_probability <= 1.0:
            raise ValueError("model_probability must be between 0 and 1")
        if self.market_price is not None and not math.isfinite(self.market_price):
            raise ValueError("market_price must be finite")
        if self.edge is not None and not math.isfinite(self.edge):
            raise ValueError("edge must be finite")
        if not math.isfinite(self.bounded_risk_fraction) or not 0.0 <= self.bounded_risk_fraction <= 1.0:
            raise ValueError("bounded_risk_fraction must be between 0 and 1")
        if self.decision == DECISION_ABSTAIN and self.bounded_risk_fraction != 0.0:
            raise ValueError("abstention must have zero bounded_risk_fraction")
        if self.decision == DECISION_BET and self.bounded_risk_fraction <= 0.0:
            raise ValueError("BET must have positive bounded_risk_fraction")

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
            "bounded_risk_fraction": self.bounded_risk_fraction,
        }


def _bounded_fraction(edge: float, policy: RiskAbstentionPolicy) -> float:
    if edge <= policy.min_positive_edge:
        return 0.0
    span = policy.edge_at_max_risk - policy.min_positive_edge
    return min(policy.max_risk_fraction, policy.max_risk_fraction * (edge - policy.min_positive_edge) / span)


def evaluate_prediction(
    prediction: PredictionRecord,
    *,
    market_price: Optional[float],
    policy: RiskAbstentionPolicy = RiskAbstentionPolicy(),
) -> RiskAbstentionDecision:
    """Evaluate one prediction without execution, bankroll use, EV, fees, or sizing side effects."""
    prediction.validate()
    policy.validate()
    if prediction.abstained:
        decision = RiskAbstentionDecision(
            RISK_ABSTENTION_SCHEMA_VERSION,
            prediction.prediction_id,
            prediction.market_id,
            prediction.outcome_id,
            market_price,
            prediction.probability,
            None if market_price is None else prediction.probability - market_price,
            DECISION_ABSTAIN,
            "prediction_already_abstained",
            0.0,
        )
        decision.validate()
        return decision

    if market_price is None:
        decision = RiskAbstentionDecision(
            RISK_ABSTENTION_SCHEMA_VERSION,
            prediction.prediction_id,
            prediction.market_id,
            prediction.outcome_id,
            None,
            prediction.probability,
            None,
            DECISION_ABSTAIN,
            "missing_historical_market_price",
            0.0,
        )
        decision.validate()
        return decision

    if not math.isfinite(market_price) or not 0.0 <= market_price <= 1.0:
        raise ValueError("market_price must be finite and between 0 and 1")

    edge = prediction.probability - market_price
    if edge <= policy.min_positive_edge:
        reason = "edge_below_abstention_threshold" if edge < policy.min_positive_edge else "edge_at_abstention_threshold"
        risk_fraction = 0.0
        decision_name = DECISION_ABSTAIN
    else:
        reason = "positive_edge_above_threshold"
        risk_fraction = _bounded_fraction(edge, policy)
        decision_name = DECISION_BET

    decision = RiskAbstentionDecision(
        RISK_ABSTENTION_SCHEMA_VERSION,
        prediction.prediction_id,
        prediction.market_id,
        prediction.outcome_id,
        market_price,
        prediction.probability,
        edge,
        decision_name,
        reason,
        risk_fraction,
    )
    decision.validate()
    return decision


def evaluate_prediction_against_history(
    prediction: PredictionRecord,
    price_points: list[HistoricalPricePoint] | tuple[HistoricalPricePoint, ...],
    *,
    policy: RiskAbstentionPolicy = RiskAbstentionPolicy(),
) -> RiskAbstentionDecision:
    """Resolve the latest eligible historical price, then apply the analysis-only gate."""
    point = select_latest_eligible_price(prediction, price_points)
    return evaluate_prediction(prediction, market_price=None if point is None else float(point.price), policy=policy)


__all__ = [
    "DECISION_ABSTAIN",
    "DECISION_BET",
    "RISK_ABSTENTION_SCHEMA_VERSION",
    "RiskAbstentionDecision",
    "RiskAbstentionPolicy",
    "evaluate_prediction",
    "evaluate_prediction_against_history",
]
