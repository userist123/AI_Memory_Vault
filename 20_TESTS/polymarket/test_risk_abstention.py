import pytest

from packages.polymarket.historical_replay import HistoricalPricePoint
from packages.polymarket.prediction_ledger import PredictionProvenance, build_prediction
from packages.polymarket.risk_abstention import (
    DECISION_ABSTAIN,
    DECISION_BET,
    AbstentionDecision,
    AbstentionPolicy,
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
    future = observed_at >= CUTOFF
    return HistoricalPricePoint(
        outcome_id="YES",
        observed_at=observed_at,
        price=price,
        requested_fidelity_minutes=60,
        source_type="clob",
        source_ref=source_ref,
        acquired_at="2026-01-01T12:02:00Z" if future else "2026-01-01T11:31:00Z",
        known_as_of="2026-01-01T12:01:00Z" if future else "2026-01-01T11:31:00Z",
    )


def test_positive_edge_above_threshold_is_bet():
    result = evaluate_prediction(prediction(0.70), market_price=0.50)
    assert result.decision == DECISION_BET
    assert result.edge == pytest.approx(0.20)
    result.validate()


def test_edge_at_or_below_threshold_abstains():
    policy = AbstentionPolicy(min_positive_edge=0.05)
    at = evaluate_prediction(prediction(0.55), market_price=0.50, policy=policy)
    below = evaluate_prediction(prediction(0.54), market_price=0.50, policy=policy)
    assert at.decision == DECISION_ABSTAIN
    assert below.decision == DECISION_ABSTAIN


def test_missing_price_abstains_closed_form():
    result = evaluate_prediction(prediction(), market_price=None)
    assert result.decision == DECISION_ABSTAIN
    assert result.reason == "missing_historical_market_price"


def test_pre_abstained_prediction_remains_abstained():
    result = evaluate_prediction(prediction(1.0, abstained=True), market_price=0.20)
    assert result.decision == DECISION_ABSTAIN
    assert result.reason == "prediction_already_abstained"


def test_history_selection_respects_prediction_cutoff():
    eligible = point("0.50", "2026-01-01T11:30:00Z", source_ref="hist:eligible")
    post_cutoff = point("0.40", "2026-01-01T12:01:00Z", source_ref="hist:future")
    result = evaluate_prediction_against_history(prediction(0.62), [post_cutoff, eligible])
    assert result.market_price == pytest.approx(0.50)
    assert result.decision == DECISION_BET


def test_contradictory_same_timestamp_fails_closed():
    a = point("0.50", source_ref="hist:a")
    b = point("0.51", source_ref="hist:b")
    #: The message comes from market_model_edge, which resolves the price, not
    #: from historical_replay. Both modules raise on contradictory points and
    #: word it differently; this test pinned the wording of the module it does
    #: not go through. What matters is that it fails closed rather than picking
    #: one of the two prices, and that is asserted below.
    with pytest.raises(ValueError, match="contradictory historical market prices"):
        evaluate_prediction_against_history(prediction(), [a, b])


def test_invalid_policy_is_rejected():
    with pytest.raises(ValueError):
        AbstentionPolicy(min_positive_edge=1.1).validate()


def test_invalid_market_price_is_rejected():
    with pytest.raises(ValueError):
        evaluate_prediction(prediction(), market_price=1.1)


def test_analysis_result_contains_no_sizing_or_execution_fields():
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
    }
    assert "quantity" not in payload
    assert "risk_fraction" not in payload
    assert "order" not in payload


def test_decision_schema_rejects_invalid_decision():
    result = evaluate_prediction(prediction(), market_price=0.50)
    invalid = AbstentionDecision(
        schema_version=result.schema_version,
        prediction_id=result.prediction_id,
        market_id=result.market_id,
        outcome_id=result.outcome_id,
        market_price=result.market_price,
        model_probability=result.model_probability,
        edge=result.edge,
        decision="UNKNOWN",
        reason=result.reason,
    )
    with pytest.raises(ValueError, match="decision must be BET or ABSTAIN"):
        invalid.validate()



def test_an_edge_exactly_at_the_threshold_abstains_despite_float_error():
    """The gate failed open on the one input it exists to refuse.

    0.55 - 0.50 is 0.050000000000000044 in binary floating point, greater than
    0.05 by 4.2e-17, so a prediction sitting precisely on a five-point
    threshold returned BET. Every pair below is nominally exactly at its
    threshold and every one of them must decline.
    """
    for probability, price, threshold in [
        (0.55, 0.50, 0.05),
        (0.70, 0.60, 0.10),
        (0.33, 0.30, 0.03),
        (0.81, 0.74, 0.07),
    ]:
        policy = AbstentionPolicy(min_positive_edge=threshold)
        result = evaluate_prediction(
            prediction(probability), market_price=price, policy=policy
        )
        assert result.decision == DECISION_ABSTAIN, (
            f"{probability} - {price} is nominally {threshold} and must abstain"
        )
        assert result.reason == "edge_at_abstention_threshold"


def test_a_real_edge_above_the_threshold_still_bets():
    """The tolerance must not swallow a genuine edge — it is 1e-9 relative, and
    a decision gate that abstains on everything is as useless as one that bets
    on everything."""
    policy = AbstentionPolicy(min_positive_edge=0.05)
    result = evaluate_prediction(prediction(0.56), market_price=0.50, policy=policy)
    assert result.decision == DECISION_BET
    assert result.reason == "positive_edge_above_threshold"
