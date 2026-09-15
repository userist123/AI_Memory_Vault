import pytest

from packages.polymarket.prediction_council import (
    PREDICTION_COUNCIL_SCHEMA_VERSION,
    build_council,
)
from packages.polymarket.prediction_ledger import PredictionProvenance, build_prediction


KNOWN = "2026-01-01T12:00:00+00:00"


def prediction(model_id, model_version, probability, abstained=False):
    return build_prediction(
        market_id="m1",
        outcome_id="yes",
        probability=probability,
        predicted_at="2026-01-01T12:01:00+00:00",
        known_as_of=KNOWN,
        provenance=PredictionProvenance(
            evidence_bundle_hash=f"evidence-{model_id}",
            snapshot_ids=("PMS-snap-1",),
            source_refs=(f"source-{model_id}",),
            source_types=("research",),
            model_id=model_id,
            model_version=model_version,
        ),
        abstained=abstained,
    )


def test_two_independent_predictions_aggregate_deterministically():
    a = prediction("model-a", "1", 0.2)
    b = prediction("model-b", "7", 0.8)
    left = build_council([a, b])
    right = build_council([b, a])
    assert left == right
    assert left.probability == pytest.approx(0.5)
    assert left.member_prediction_ids == tuple(sorted((a.prediction_id, b.prediction_id)))
    assert left.provenance.model_id == "prediction-council"
    assert left.provenance.model_version == PREDICTION_COUNCIL_SCHEMA_VERSION


def test_member_order_does_not_change_id_or_provenance():
    a = prediction("model-a", "1", 0.3)
    b = prediction("model-b", "1", 0.7)
    assert build_council([a, b]).as_dict() == build_council([b, a]).as_dict()


def test_same_model_identity_is_not_independent():
    with pytest.raises(ValueError, match="independent model identities"):
        build_council([prediction("same", "1", 0.3), prediction("same", "1", 0.9)])


def test_mismatched_information_cutoff_is_rejected():
    a = prediction("model-a", "1", 0.3)
    b = prediction("model-b", "1", 0.7)
    b = build_prediction(
        market_id=b.market_id,
        outcome_id=b.outcome_id,
        probability=b.probability,
        predicted_at=b.predicted_at,
        known_as_of="2026-01-01T11:59:00+00:00",
        provenance=b.provenance,
    )
    with pytest.raises(ValueError, match="same known_as_of"):
        build_council([a, b])


def test_different_market_or_outcome_is_rejected():
    a = prediction("model-a", "1", 0.3)
    b = build_prediction(
        market_id="m2",
        outcome_id="yes",
        probability=0.7,
        predicted_at=a.predicted_at,
        known_as_of=a.known_as_of,
        provenance=PredictionProvenance(
            evidence_bundle_hash="evidence-b",
            snapshot_ids=("PMS-snap-1",),
            source_refs=("source-b",),
            source_types=("research",),
            model_id="model-b",
            model_version="1",
        ),
    )
    with pytest.raises(ValueError, match="same market and outcome"):
        build_council([a, b])


def test_abstentions_are_excluded_without_becoming_confidence():
    a = prediction("model-a", "1", 0.2)
    b = prediction("model-b", "1", 0.0, abstained=True)
    council = build_council([a, b])
    assert council.probability == pytest.approx(0.2)
    assert council.abstained is False


def test_all_members_abstain_produces_explicit_abstention():
    a = prediction("model-a", "1", 0.0, abstained=True)
    b = prediction("model-b", "1", 1.0, abstained=True)
    council = build_council([a, b])
    assert council.abstained is True
    assert council.probability == 0.0


def test_single_prediction_is_not_a_council():
    with pytest.raises(ValueError, match="at least two"):
        build_council([prediction("only", "1", 0.5)])


def test_forged_council_id_is_rejected():
    a = prediction("model-a", "1", 0.4)
    b = prediction("model-b", "1", 0.6)
    council = build_council([a, b])
    forged = council.__class__(
        council_id="COUNCIL-forged",
        **{k: getattr(council, k) for k in council.__dataclass_fields__ if k != "council_id"},
    )
    with pytest.raises(ValueError, match="canonical council content"):
        forged.validate()
