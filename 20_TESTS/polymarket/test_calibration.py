import math

import pytest

from packages.polymarket.calibration import (
    CALIBRATION_SCHEMA_VERSION,
    CalibrationObservation,
    observe_prediction,
    score_calibration,
)
from packages.polymarket.market_snapshot import (
    MarketLifecycle,
    PolymarketMarket,
    PriceObservation,
    ResolutionMetadata,
    build_snapshot,
)
from packages.polymarket.prediction_ledger import PredictionProvenance, build_prediction


PREDICTION_TIME = "2026-01-01T12:00:00+00:00"
EVIDENCE_TIME = "2026-01-01T11:59:00+00:00"
RESOLUTION_TIME = "2026-01-01T13:00:00+00:00"


def make_snapshot(*, snapshot_id_hint="evidence", known_as_of=EVIDENCE_TIME, resolution=None, data_quality="verified"):
    return build_snapshot(
        market=PolymarketMarket(
            market_id="m1",
            condition_id="c1",
            question="Will event happen?",
            description=None,
            category="test",
            market_type="binary",
            outcomes=("Yes", "No"),
            outcome_ids=("yes", "no"),
            lifecycle=MarketLifecycle.RESOLVED if resolution else MarketLifecycle.CLOSED,
            created_at=None,
            start_at=None,
            close_at="2026-01-01T12:30:00+00:00",
            expected_resolution_at=None,
            resolution_source="official" if resolution else None,
            resolution_rule_text="test rule" if resolution else None,
            cancellation_state=None,
            slug="m1",
        ),
        snapshot_at=known_as_of,
        acquired_at=known_as_of,
        known_as_of=known_as_of,
        price_observations=(PriceObservation("yes", "0.50", known_as_of, "test"),),
        liquidity=None,
        volume=None,
        source_type="test",
        source_ref=snapshot_id_hint,
        data_quality=data_quality,
        source_payload_hash=f"hash-{snapshot_id_hint}",
        resolution=resolution,
    )


def make_prediction(probability=0.8, *, evidence_id, known_as_of=PREDICTION_TIME):
    return build_prediction(
        market_id="m1",
        outcome_id="yes",
        probability=probability,
        predicted_at=PREDICTION_TIME,
        known_as_of=known_as_of,
        provenance=PredictionProvenance(
            evidence_bundle_hash="bundle-1",
            snapshot_ids=(evidence_id,),
            source_refs=("source-1",),
            source_types=("research",),
            model_id="model-a",
            model_version="1",
        ),
    )


def make_resolution(*, winner=("yes",), known_at=RESOLUTION_TIME):
    return ResolutionMetadata(
        status="resolved",
        outcome_ids=tuple(winner),
        resolution_source="official-resolution",
        resolution_rule_text="test rule",
        known_at=known_at,
    )


def test_observation_requires_verified_resolution_and_no_post_cutoff_evidence():
    evidence = make_snapshot(snapshot_id_hint="evidence-1")
    prediction = make_prediction(0.8, evidence_id=evidence.snapshot_id)
    resolution = make_snapshot(
        snapshot_id_hint="resolution-1",
        known_as_of=RESOLUTION_TIME,
        resolution=make_resolution(),
    )
    observed = observe_prediction(
        prediction,
        evidence_snapshots={evidence.snapshot_id: evidence},
        resolution_snapshot=resolution,
    )
    assert observed.prediction_id == prediction.prediction_id
    assert observed.target == 1
    assert observed.probability == pytest.approx(0.8)


def test_post_cutoff_evidence_is_rejected():
    leaked = make_snapshot(snapshot_id_hint="evidence-1", known_as_of=RESOLUTION_TIME)
    prediction = make_prediction(0.8, evidence_id=leaked.snapshot_id)
    resolution = make_snapshot(
        snapshot_id_hint="resolution-1",
        known_as_of=RESOLUTION_TIME,
        resolution=make_resolution(),
    )
    with pytest.raises(ValueError, match="known after the prediction cutoff"):
        observe_prediction(
            prediction,
            evidence_snapshots={leaked.snapshot_id: leaked},
            resolution_snapshot=resolution,
        )


def test_resolution_known_at_must_postdate_prediction_cutoff():
    evidence = make_snapshot(snapshot_id_hint="evidence-1")
    prediction = make_prediction(0.8, evidence_id=evidence.snapshot_id)
    resolution = make_snapshot(
        snapshot_id_hint="resolution-1",
        known_as_of=PREDICTION_TIME,
        resolution=make_resolution(known_at=PREDICTION_TIME),
    )
    with pytest.raises(ValueError, match="must precede resolution knowledge"):
        observe_prediction(
            prediction,
            evidence_snapshots={evidence.snapshot_id: evidence},
            resolution_snapshot=resolution,
        )


def test_non_verified_resolution_is_rejected():
    evidence = make_snapshot(snapshot_id_hint="evidence-1")
    prediction = make_prediction(0.8, evidence_id=evidence.snapshot_id)
    resolution = make_snapshot(
        snapshot_id_hint="resolution-1",
        known_as_of=RESOLUTION_TIME,
        resolution=make_resolution(),
        data_quality="synthetic",
    )
    with pytest.raises(ValueError, match="verified resolution"):
        observe_prediction(
            prediction,
            evidence_snapshots={evidence.snapshot_id: evidence},
            resolution_snapshot=resolution,
        )


def test_unknown_evidence_snapshot_is_rejected():
    prediction = make_prediction(0.8, evidence_id="PMS-missing")
    resolution = make_snapshot(
        snapshot_id_hint="resolution-1",
        known_as_of=RESOLUTION_TIME,
        resolution=make_resolution(),
    )
    with pytest.raises(ValueError, match="missing evidence snapshot"):
        observe_prediction(
            prediction,
            evidence_snapshots={},
            resolution_snapshot=resolution,
        )


def test_calibration_metrics_and_bins_are_deterministic():
    observations = [
        CalibrationObservation("p1", "m1", "yes", 0.1, 0, EVIDENCE_TIME, RESOLUTION_TIME),
        CalibrationObservation("p2", "m1", "yes", 0.2, 0, EVIDENCE_TIME, RESOLUTION_TIME),
        CalibrationObservation("p3", "m1", "yes", 0.8, 1, EVIDENCE_TIME, RESOLUTION_TIME),
        CalibrationObservation("p4", "m1", "yes", 0.9, 1, EVIDENCE_TIME, RESOLUTION_TIME),
    ]
    report = score_calibration(observations, bin_count=2)
    assert report.schema_version == CALIBRATION_SCHEMA_VERSION
    assert report.observation_count == 4
    assert report.brier_score == pytest.approx(0.025)
    assert report.log_loss == pytest.approx((-math.log(0.9) - math.log(0.8) - math.log(0.8) - math.log(0.9)) / 4)
    assert report.bins[0].count == 2
    assert report.bins[0].mean_predicted_probability == pytest.approx(0.15)
    assert report.bins[0].empirical_rate == pytest.approx(0.0)
    assert report.bins[1].count == 2
    assert report.bins[1].mean_predicted_probability == pytest.approx(0.85)
    assert report.bins[1].empirical_rate == pytest.approx(1.0)
    assert report.as_dict() == score_calibration(list(reversed(observations)), bin_count=2).as_dict()


def test_extreme_probability_log_loss_is_finite():
    report = score_calibration(
        [CalibrationObservation("p", "m1", "yes", 0.0, 1, EVIDENCE_TIME, RESOLUTION_TIME)]
    )
    assert math.isfinite(report.log_loss)
    assert report.log_loss > 0


def test_invalid_bin_count_is_rejected():
    observation = CalibrationObservation("p", "m1", "yes", 0.5, 1, EVIDENCE_TIME, RESOLUTION_TIME)
    with pytest.raises(ValueError, match="bin_count"):
        score_calibration([observation], bin_count=1)
