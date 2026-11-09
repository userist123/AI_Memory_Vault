"""Deterministic multi-prediction council built on the Phase 3 ledger contract."""
from __future__ import annotations

from dataclasses import dataclass
from typing import Iterable
import hashlib
import json

from .prediction_ledger import PredictionProvenance, PredictionRecord

PREDICTION_COUNCIL_SCHEMA_VERSION = "polymarket-prediction-council.v1"


def _canonical(value: object) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True)


def _hash(value: object) -> str:
    return hashlib.sha256(_canonical(value).encode("utf-8")).hexdigest()


@dataclass(frozen=True)
class CouncilRecord:
    council_id: str
    schema_version: str
    market_id: str
    outcome_id: str
    probability: float
    predicted_at: str
    known_as_of: str
    member_prediction_ids: tuple[str, ...]
    provenance: PredictionProvenance
    abstained: bool = False

    def canonical_payload(self) -> dict[str, object]:
        return {
            "schema_version": self.schema_version,
            "market_id": self.market_id,
            "outcome_id": self.outcome_id,
            "probability": self.probability,
            "predicted_at": self.predicted_at,
            "known_as_of": self.known_as_of,
            "member_prediction_ids": list(self.member_prediction_ids),
            "provenance": self.provenance.as_dict(),
            "abstained": self.abstained,
        }

    def expected_id(self) -> str:
        return f"COUNCIL-{_hash(self.canonical_payload())[:24]}"

    def validate(self) -> None:
        if self.schema_version != PREDICTION_COUNCIL_SCHEMA_VERSION:
            raise ValueError("unsupported prediction council schema version")
        if not self.market_id or not self.outcome_id:
            raise ValueError("market_id and outcome_id are required")
        if not self.member_prediction_ids or len(set(self.member_prediction_ids)) != len(self.member_prediction_ids):
            raise ValueError("member_prediction_ids must be non-empty and unique")
        if not 0.0 <= self.probability <= 1.0:
            raise ValueError("probability must be between 0 and 1")
        if self.known_as_of > self.predicted_at:
            raise ValueError("known_as_of cannot be after predicted_at")
        self.provenance.validate()
        if self.council_id != self.expected_id():
            raise ValueError("council_id does not match canonical council content")
        if self.abstained and self.probability not in (0.0, 1.0):
            raise ValueError("abstained councils must use an explicit boundary probability")

    def as_dict(self) -> dict[str, object]:
        self.validate()
        return {"council_id": self.council_id, **self.canonical_payload()}


def build_council(predictions: Iterable[PredictionRecord]) -> CouncilRecord:
    """Aggregate independent ledger predictions by an unweighted arithmetic mean.

    No calibration or confidence weighting is performed in this phase. All members
    must describe the same market/outcome and exact information cutoff. Abstained
    members are excluded from the mean; if all members abstain, the council abstains
    with boundary probability 0.0 rather than inventing a consensus probability.
    """
    members = tuple(predictions)
    if len(members) < 2:
        raise ValueError("a council requires at least two predictions")
    for prediction in members:
        prediction.validate()
    first = members[0]
    if any(p.market_id != first.market_id or p.outcome_id != first.outcome_id for p in members):
        raise ValueError("all council members must target the same market and outcome")
    if any(p.known_as_of != first.known_as_of for p in members):
        raise ValueError("all council members must share the same known_as_of")
    model_keys = [(p.provenance.model_id, p.provenance.model_version) for p in members]
    if len(set(model_keys)) != len(model_keys):
        raise ValueError("council members must have independent model identities and versions")
    active = tuple(p for p in members if not p.abstained)
    abstained = not active
    probability = 0.0 if abstained else sum(p.probability for p in active) / len(active)
    predicted_at = max(p.predicted_at for p in members)
    prediction_ids = tuple(sorted(p.prediction_id for p in members))
    snapshot_ids = tuple(sorted({sid for p in members for sid in p.provenance.snapshot_ids}))
    source_refs = prediction_ids
    source_types = tuple("prediction_record" for _ in prediction_ids)
    evidence_hash = _hash(sorted((p.prediction_id, p.provenance.evidence_bundle_hash) for p in members))
    provenance = PredictionProvenance(
        evidence_bundle_hash=evidence_hash,
        snapshot_ids=snapshot_ids,
        source_refs=source_refs,
        source_types=source_types,
        model_id="prediction-council",
        model_version=PREDICTION_COUNCIL_SCHEMA_VERSION,
    )
    provisional = CouncilRecord(
        council_id="",
        schema_version=PREDICTION_COUNCIL_SCHEMA_VERSION,
        market_id=first.market_id,
        outcome_id=first.outcome_id,
        probability=probability,
        predicted_at=predicted_at,
        known_as_of=first.known_as_of,
        member_prediction_ids=prediction_ids,
        provenance=provenance,
        abstained=abstained,
    )
    record = CouncilRecord(council_id=provisional.expected_id(), **{k: getattr(provisional, k) for k in provisional.__dataclass_fields__ if k != "council_id"})
    record.validate()
    return record


__all__ = ["PREDICTION_COUNCIL_SCHEMA_VERSION", "CouncilRecord", "build_council"]
