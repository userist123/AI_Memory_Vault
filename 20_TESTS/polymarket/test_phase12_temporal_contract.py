from __future__ import annotations

from packages.polymarket.historical_replay import HistoricalPricePoint
from packages.polymarket.prediction_ledger import PredictionProvenance, build_prediction


def _prediction():
    provenance = PredictionProvenance(
        evidence_bundle_hash="evidence-hash",
        snapshot_ids=("snapshot-1",),
        source_refs=("source-1",),
        source_types=("test",),
        model_id="test-model",
        model_version="1",
    )
    return build_prediction(
        market_id="market-1",
        outcome_id="outcome-yes",
        probability=0.75,
        predicted_at="2026-01-01T12:00:00Z",
        known_as_of="2026-01-01T11:00:00Z",
        provenance=provenance,
    )


def test_historical_price_must_have_explicit_temporal_provenance() -> None:
    point = HistoricalPricePoint(
        outcome_id="outcome-yes",
        observed_at="2026-01-01T10:00:00Z",
        price="0.50",
        requested_fidelity_minutes=None,
        source_type="test",
        source_ref="source-1",
        acquired_at="2026-01-01T10:05:00Z",
        known_as_of="2026-01-01T10:05:00Z",
    )
    point.validate()


def test_post_cutoff_market_knowledge_is_not_eligible() -> None:
    prediction = _prediction()
    point = HistoricalPricePoint(
        outcome_id=prediction.outcome_id,
        observed_at="2026-01-01T10:00:00Z",
        price="0.50",
        requested_fidelity_minutes=None,
        source_type="test",
        source_ref="source-1",
        acquired_at="2026-01-01T11:01:00Z",
        known_as_of="2026-01-01T11:01:00Z",
    )
    point.validate()
    assert point.known_as_of > prediction.known_as_of


def test_observation_after_cutoff_is_not_eligible() -> None:
    prediction = _prediction()
    point = HistoricalPricePoint(
        outcome_id=prediction.outcome_id,
        observed_at="2026-01-01T11:00:01Z",
        price="0.50",
        requested_fidelity_minutes=None,
        source_type="test",
        source_ref="source-1",
        acquired_at="2026-01-01T11:00:02Z",
        known_as_of="2026-01-01T11:00:02Z",
    )
    point.validate()
    assert point.observed_at > prediction.known_as_of
