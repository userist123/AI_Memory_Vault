"""Deterministic analysis-only confidence gating and abstention."""
from __future__ import annotations

from dataclasses import dataclass
import math
from typing import Optional

from .prediction_ledger import PredictionRecord

RISK_ABSTENTION_SCHEMA_VERSION = "polymarket-risk-abstention.v1"
DECISION_PROCEED = "PROCEED"
DECISION_ABSTAIN = "ABSTAIN"


@dataclass(frozen=True)
class AbstentionPolicy:
    """Pure analysis policy with no execution or financial side effects."""

    min_probability: float = 0.55
    min_distance_from_midpoint: float = 0.05

    def validate(self) -> None:
        values = (self.min_probability, self.min_distance_from_midpoint)
        if not all(math.isfinite(value) for value in values):
            raise ValueError("abstention policy values must be finite")
        if not 0.5 < self.min_probability <= 1.0:
            raise ValueError("min_probability must be in (0.5, 1]")
        if not 0.0 < self.min_distance_from_midpoint <= 0.5:
            raise ValueError("min_distance_from_midpoint must be in (0, 0.5]")
        if self.min_probability - 0.5 < self.min_distance_from_midpoint:
            raise ValueError("min_probability must imply at least min_distance_from_midpoint")


@dataclass(frozen=True)
class AbstentionDecision:
    schema_version: str
    prediction_id: str
    market_id: str
    outcome_id: str
    model_probability: float
    decision: str
    reason: str

    def validate(self) -> None:
        if self.schema_version != RISK_ABSTENTION_SCHEMA_VERSION:
            raise ValueError("unsupported risk-abstention schema version")
        if self.decision not in (DECISION_PROCEED, DECISION_ABSTAIN):
            raise ValueError("decision must be PROCEED or ABSTAIN")
        if not self.prediction_id or not self.market_id or not self.outcome_id:
            raise ValueError("prediction and market identifiers are required")
        if not math.isfinite(self.model_probability) or not 0.0 <= self.model_probability <= 1.0:
            raise ValueError("model_probability must be between 0 and 1")

    def as_dict(self) -> dict[str, object]:
        self.validate()
        return {
            "schema_version": self.schema_version,
            "prediction_id": self.prediction_id,
            "market_id": self.market_id,
            "outcome_id": self.outcome_id,
            "model_probability": self.model_probability,
            "decision": self.decision,
            "reason": self.reason,
        }


def evaluate_prediction(
    prediction: PredictionRecord,
    *,
    policy: AbstentionPolicy = AbstentionPolicy(),
) -> AbstentionDecision:
    """Apply a deterministic confidence gate without execution, sizing, or monetary outputs."""
    prediction.validate()
    policy.validate()

    if prediction.abstained:
        decision = AbstentionDecision(
            RISK_ABSTENTION_SCHEMA_VERSION,
            prediction.prediction_id,
            prediction.market_id,
            prediction.outcome_id,
            prediction.probability,
            DECISION_ABSTAIN,
            "prediction_already_abstained",
        )
        decision.validate()
        return decision

    probability = prediction.probability
    distance = abs(probability - 0.5)
    if probability < policy.min_probability or distance < policy.min_distance_from_midpoint:
        reason = "confidence_below_abstention_threshold"
        decision_name = DECISION_ABSTAIN
    else:
        reason = "confidence_above_abstention_threshold"
        decision_name = DECISION_PROCEED

    decision = AbstentionDecision(
        RISK_ABSTENTION_SCHEMA_VERSION,
        prediction.prediction_id,
        prediction.market_id,
        prediction.outcome_id,
        probability,
        decision_name,
        reason,
    )
    decision.validate()
    return decision


__all__ = [
    "AbstentionPolicy",
    "AbstentionDecision",
    "DECISION_ABSTAIN",
    "DECISION_PROCEED",
    "RISK_ABSTENTION_SCHEMA_VERSION",
    "evaluate_prediction",
]
