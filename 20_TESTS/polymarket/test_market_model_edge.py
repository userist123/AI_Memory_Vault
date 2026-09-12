import pytest

from packages.polymarket.historical_replay import HistoricalPricePoint
from packages.polymarket.market_model_edge import (
    MARKET_MODEL_EDGE_SCHEMA_VERSION,
    build_edge_observation,
    compare_predictions_to_market,
    select_latest_eligible_price,
)
from packages.polymarket.prediction_ledger import PredictionProvenance, build_prediction

PREDICTION_TIME = "2026-01-01T12:00:00+00:00"
EARLY_TIME = "2026-01-01T11:55:00+00:00"
LATE_TIME = "2026-01-01T12:05:00+00:00"


def make_prediction(probability=0.8, prediction_id_hint="p1", known_as_of=PREDICTION_TIME):
    return build_prediction(
        market_id="m1",
        outcome_id="yes",
        probability=probability,
        predicted_at=PREDICTION_TIME,
        known_as_of=known_as_of,
        provenance=PredictionProvenance(
            evidence_bundle_hash=f"bundle-{prediction_id_hint}",
            snapshot_ids=(f"snapshot-{prediction_id_hint}",),
            source_refs=(f"source-{prediction_id_hint}",),
            source_types=("research",),
            model_id=f"model-{prediction_id_hint}",
            model_version="1",
        ),
    )


def make_price(price="0.50", observed_at=EARLY_TIME, known_as_of=EARLY_TIME, source_ref="price-1", outcome_id="yes"):
    return HistoricalPricePoint(
        outcome_id=outcome_id,
        observed_at=observed_at,
        price=price,
        requested_fidelity_minutes=1,
        source_type="clob-price-history",
        source_ref=source_ref,
        acquired_at=known_as_of,
        known_as_of=known_as_of,
    )


def test_build_edge_is_content_bound_and_expected():
    prediction = make_prediction(0.8)
    point = make_price("0.5")
    edge = build_edge_observation(prediction, point)
    assert edge.schema_version == MARKET_MODEL_EDGE_SCHEMA_VERSION
    assert edge.model_probability == pytest.approx(0.8)
    assert edge.market_price == pytest.approx(0.5)
    assert edge.edge == pytest.approx(0.3)
    assert edge.edge_id.startswith("EDGE-")
    edge.validate()


def test_latest_eligible_price_uses_only_pre_cutoff_information():
    prediction = make_prediction()
    early = make_price("0.4", observed_at=EARLY_TIME, known_as_of=EARLY_TIME, source_ref="early")
    later_known_before_cutoff = make_price(
        "0.6", observed_at="2026-01-01T11:59:00+00:00", known_as_of="2026-01-01T11:59:00+00:00", source_ref="latest"
    )
    post_cutoff = make_price("0.9", observed_at=LATE_TIME, known_as_of=LATE_TIME, source_ref="future")
    selected = select_latest_eligible_price(prediction, (post_cutoff, early, later_known_before_cutoff))
    assert selected.source_ref == "latest"


def test_post_cutoff_price_is_rejected_when_used_directly():
    prediction = make_prediction()
    point = make_price("0.5", observed_at=LATE_TIME, known_as_of=LATE_TIME)
    with pytest.raises(ValueError, match="known after the prediction cutoff"):
        build_edge_observation(prediction, point)


def test_post_cutoff_observation_time_is_rejected_when_known_before_cutoff():
    prediction = make_prediction()
    point = make_price("0.5", observed_at=LATE_TIME, known_as_of=PREDICTION_TIME)
    with pytest.raises(ValueError, match="after the prediction cutoff"):
        build_edge_observation(prediction, point)


def test_invalid_market_price_is_rejected():
    prediction = make_prediction()
    point = make_price("1.2")
    with pytest.raises(ValueError, match="between 0 and 1"):
        build_edge_observation(prediction, point)


def test_outcome_mismatch_is_rejected():
    prediction = make_prediction()
    point = make_price("0.5", outcome_id="no")
    with pytest.raises(ValueError, match="outcome_id"):
        build_edge_observation(prediction, point)


def test_contradictory_same_timestamp_prices_fail_closed():
    prediction = make_prediction()
    a = make_price("0.4", source_ref="a")
    b = make_price("0.5", source_ref="b")
    with pytest.raises(ValueError, match="contradictory historical market prices"):
        select_latest_eligible_price(prediction, (a, b))


def test_summary_is_deterministic_and_sorted_by_prediction_id():
    p1 = make_prediction(0.8, prediction_id_hint="one")
    p2 = make_prediction(0.2, prediction_id_hint="two")
    prices = (
        make_price("0.5", source_ref="p1-price"),
        make_price("0.1", source_ref="p2-price"),
    )
    # Both market prices target the same outcome and cutoff; each prediction gets the latest same price.
    # Use one common price to exercise deterministic ordering without adding implicit market selection semantics.
    summary = compare_predictions_to_market((p2, p1), (make_price("0.5", source_ref="common"),))
    assert summary.observation_count == 2
    assert [item.prediction_id for item in summary.observations] == sorted([p1.prediction_id, p2.prediction_id])
    assert summary.mean_edge == pytest.approx(0.0)
    assert summary.mean_absolute_edge == pytest.approx(0.3)
    assert summary.positive_edge_count == 1
    assert summary.negative_edge_count == 1
    assert summary.zero_edge_count == 0
    assert summary.as_dict() == compare_predictions_to_market((p1, p2), (make_price("0.5", source_ref="common"),)).as_dict()


def test_no_market_price_known_before_cutoff_is_rejected():
    prediction = make_prediction()
    post_cutoff = make_price("0.5", observed_at=LATE_TIME, known_as_of=LATE_TIME)
    with pytest.raises(ValueError, match="no market price"):
        select_latest_eligible_price(prediction, (post_cutoff,))


def test_zero_edge_is_counted_separately():
    prediction = make_prediction(0.5)
    summary = compare_predictions_to_market((prediction,), (make_price("0.5"),))
    assert summary.zero_edge_count == 1
    assert summary.positive_edge_count == 0
    assert summary.negative_edge_count == 0
    assert summary.mean_edge == pytest.approx(0.0)
