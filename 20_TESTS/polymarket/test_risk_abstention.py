import pytest

from packages.polymarket.historical_replay import HistoricalPricePoint
from packages.polymarket.prediction_ledger import PredictionProvenance, build_prediction
from packages.polymarket.risk_abstention import (
    DECISION_ABSTAIN,
    DECISION_BET,
    RiskAbstentionPolicy,
    evaluate_prediction,
    evaluate_prediction_against_history,
)

CUTOFF = "2026-01-01T12:00:00Z"


def provenance():
    return PredictionProvenance(
        evidence_bundle_hash="ev-1",
        snapshot_ids=("PMS-1",),
        source_refs=("snapshot:1",),
        source_types=("snapshot",),
        model_id="model-a",
        model_version="1",
    )


def prediction(probability=0.62, *, abstained=False):
    return build_prediction(
        market_id="m-1",
        outcome_id="YES",
        probability=probability,
        predicted_at=CUTOFF,
        known_as_of="2026-01-01T11:59:00Z",
        provenance=provenance(),
        abstained=abstained,
    )


def point(price="0.50", observed_at="2026-01-01T11:30:00Z", *, source_ref="hist:1"):
    return HistoricalPricePoint(
        outcome_id="YES",
        observed_at=observed_at,
        price=price,
        requested_fidelity_minutes=60,
        source_type="clob",
        source_ref=source_ref,
        acquired_at="2026-01-01T11:31:00Z" if observed_at < CUTOFF else "2026-01-01T12:02:00Z",
        known_as_of="2026-01-01T11:31:00Z" if observed_at < CUTOFF else "2026-01-01T12:01:00Z",
    )


def test_positive_edge_above_threshold_is_bet_with_bounded_risk():
    result = evaluate_prediction(prediction(0.70), market_price=0.50)
    assert result.decision == DECISION_BET
    assert result.edge == pytest.approx(0.20)
    assert result.bounded_risk_fraction == pytest.approx(0.02)
    result.validate()


def test_edge_at_or_below_threshold_abstains():
    policy = RiskAbstentionPolicy(min_positive_edge=0.05, max_risk_fraction=0.02, edge_at_max_risk=0.10)
    at = evaluate_prediction(prediction(0.55), market_price=0.50, policy=policy)
    below = evaluate_prediction(prediction(0.54), market_price=0.50, policy=policy)
    assert at.decision == DECISION_ABSTAIN
    assert below.decision == DECISION_ABSTAIN
    assert at.bounded_risk_fraction == 0.0
    assert below.bounded_risk_fraction == 0.0


def test_missing_price_abstains_closed_form():
    result = evaluate_prediction(prediction(), market_price=None)
    assert result.decision == DECISION_ABSTAIN
    assert result.reason == "missing_historical_market_price"
    assert result.bounded_risk_fraction == 0.0


def test_pre_abstained_prediction_remains_abstained():
    result = evaluate_prediction(prediction(1.0, abstained=True), market_price=0.20)
    assert result.decision == DECISION_ABSTAIN
    assert result.reason == "prediction_already_abstained"
    assert result.bounded_risk_fraction == 0.0


def test_fraction_is_monotone_and_capped():
    policy = RiskAbstentionPolicy()
    low = evaluate_prediction(prediction(0.61), market_price=0.50, policy=policy)
    high = evaluate_prediction(prediction(0.75), market_price=0.50, policy=policy)
    assert 0.0 < low.bounded_risk_fraction < high.bounded_risk_fraction
    assert high.bounded_risk_fraction == policy.max_risk_fraction


def test_history_selection_respects_prediction_cutoff():
    eligible = point("0.50", "2026-01-01T11:30:00Z", source_ref="hist:eligible")
    post_cutoff = point("0.40", "2026-01-01T12:01:00Z", source_ref="hist:future")
    result = evaluate_prediction_against_history(prediction(0.62), [post_cutoff, eligible])
    assert result.market_price == pytest.approx(0.50)
    assert result.decision == DECISION_BET


def test_contradictory_same_timestamp_fails_closed():
    a = point("0.50", source_ref="hist:a")
    b = point("0.51", source_ref="hist:b")
    with pytest.raises(ValueError, match="contradictory historical price point"):
        evaluate_prediction_against_history(prediction(), [a, b])


def test_invalid_policy_is_rejected():
    with pytest.raises(ValueError):
        RiskAbstentionPolicy(min_positive_edge=0.10, edge_at_max_risk=0.10).validate()


def test_invalid_market_price_is_rejected():
    with pytest.raises(ValueError):
        evaluate_prediction(prediction(), market_price=1.1)


def test_analysis_result_has_no_execution_fields():
    result = evaluate_prediction(prediction(), market_price=0.50)
    payload = result.as_dict()
    assert set(payload) == {
        "schema_version",
        "prediction_id",
        "market_id",
        "outcome_id",
        "market_price",
        "model_probability",
        "edge",
        "decision",
        "reason",
        "bounded_risk_fraction",
    }
    assert "order" not in payload
    assert "quantity" not in payload
