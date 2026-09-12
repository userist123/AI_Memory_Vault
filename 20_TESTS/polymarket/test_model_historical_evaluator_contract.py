from dataclasses import replace

import pytest

from packages.polymarket.model_historical_evaluator_contract import (
    EvaluatorPrediction,
    TemporalTapePoint,
    evaluate_prediction,
    select_eligible_point,
)


def point(ts, price, known=None, acquired=None):
    if known is None:
        known = ts
    if acquired is None:
        acquired_dt = __import__("datetime").datetime.fromisoformat(known.replace("Z", "+00:00"))
        acquired = (acquired_dt + __import__("datetime").timedelta(minutes=1)).isoformat().replace("+00:00", "Z")
    return TemporalTapePoint(
        market_id="m1",
        outcome_id="yes",
        observed_at=ts,
        price=price,
        known_as_of=known,
        acquired_at=acquired,
    )


def prediction(cutoff="2023-11-14T22:00:00Z"):
    return EvaluatorPrediction(
        market_id="m1",
        outcome_id="yes",
        probability=0.70,
        known_as_of=cutoff,
    )


def test_selects_latest_fully_provenanced_pre_cutoff_point():
    selected = select_eligible_point(
        prediction(),
        [
            point("2023-11-14T20:30:00Z", 0.40, "2023-11-14T20:31:00Z", "2023-11-14T20:32:00Z"),
            point("2023-11-14T21:30:00Z", 0.60, "2023-11-14T21:31:00Z", "2023-11-14T21:32:00Z"),
            point("2023-11-14T22:00:01Z", 0.90, "2023-11-14T22:00:02Z", "2023-11-14T22:00:03Z"),
        ],
    )
    assert selected.observed_at == "2023-11-14T21:30:00Z"
    assert selected.price == 0.60


def test_fails_closed_when_provenance_is_missing():
    missing = replace(
        point("2023-11-14T20:30:00Z", 0.40, "2023-11-14T20:31:00Z", "2023-11-14T20:32:00Z"),
        known_as_of=None,
    )
    with pytest.raises(ValueError, match="explicit known_as_of"):
        select_eligible_point(prediction(), [missing])


def test_rejects_resolution_known_at_cutoff():
    with pytest.raises(ValueError, match="resolution cannot be known"):
        evaluate_prediction(
            prediction(),
            [point("2023-11-14T21:30:00Z", 0.60, "2023-11-14T21:31:00Z", "2023-11-14T21:32:00Z")],
            resolved_outcome_id="yes",
            resolution_known_at="2023-11-14T22:00:00Z",
        )


def test_rejects_point_known_after_cutoff_even_when_observed_before_cutoff():
    late_knowledge = point(
        "2023-11-14T21:00:00Z",
        0.90,
        known="2023-11-14T22:00:01Z",
        acquired="2023-11-14T22:00:02Z",
    )
    with pytest.raises(ValueError, match="no temporally eligible"):
        select_eligible_point(prediction(), [late_knowledge])


def test_rejects_market_mismatch():
    other_market = replace(
        point("2023-11-14T21:30:00Z", 0.60, "2023-11-14T21:31:00Z", "2023-11-14T21:32:00Z"),
        market_id="m2",
    )
    with pytest.raises(ValueError, match="no temporally eligible"):
        select_eligible_point(prediction(), [other_market])


def test_settlement_and_edge_are_deterministic():
    result = evaluate_prediction(
        prediction(),
        [point("2023-11-14T21:30:00Z", 0.50, "2023-11-14T21:31:00Z", "2023-11-14T21:32:00Z")],
        resolved_outcome_id="yes",
        resolution_known_at="2023-11-15T10:00:00Z",
    )
    assert result.raw_edge == pytest.approx(0.20)
    assert result.settled_units == pytest.approx(2.0)
    assert result.selected_known_as_of == "2023-11-14T21:31:00Z"
    assert result.selected_acquired_at == "2023-11-14T21:32:00Z"
