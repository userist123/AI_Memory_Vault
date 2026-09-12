from __future__ import annotations

import json
import pytest

from packages.polymarket.historical_paper_replay import build_market_bundle
from packages.polymarket.model_historical_evaluator import evaluate_prediction, select_eligible_tape_point
from packages.polymarket.prediction_ledger import PredictionProvenance, build_prediction


def _provenance() -> PredictionProvenance:
    return PredictionProvenance(
        evidence_bundle_hash="evidence",
        snapshot_ids=("snapshot-1",),
        source_refs=("source-1",),
        source_types=("test",),
        model_id="test-model",
        model_version="1",
    )


def _prediction(cutoff: str = "2023-11-15T12:00:00Z"):
    return build_prediction(
        market_id="market-1",
        outcome_id="token-yes",
        probability=0.75,
        predicted_at=cutoff,
        known_as_of=cutoff,
        provenance=_provenance(),
    )


def _bundle(resolution_known_at: str = "2026-01-01T00:00:00Z"):
    payload = {
        "id": "market-1",
        "question": "Will A happen?",
        "closed": True,
        "outcomes": json.dumps(["Yes", "No"]),
        "clobTokenIds": json.dumps(["token-yes", "token-no"]),
        "outcomePrices": json.dumps(["1", "0"]),
    }
    history = {
        "token-yes": [
            {"t": 1700000000, "p": 0.40},
            {"t": 1700086400, "p": 0.60},
        ],
        "token-no": [
            {"t": 1700000000, "p": 0.60},
            {"t": 1700086400, "p": 0.40},
        ],
    }
    return build_market_bundle(payload, resolution_known_at=resolution_known_at, history_by_token=history)


def test_evaluator_selects_latest_pre_cutoff_point_and_settles() -> None:
    prediction = _prediction()
    result = evaluate_prediction(prediction, _bundle())
    assert result.selected_observed_at == "2023-11-15T00:00:00Z"
    assert result.market_price == pytest.approx(0.60)
    assert result.edge == pytest.approx(0.15)
    assert result.settlement_win is True
    assert result.settlement_units_per_notional == pytest.approx(1.0 / 0.60)


def test_evaluator_rejects_resolution_known_at_cutoff() -> None:
    prediction = _prediction()
    bundle = _bundle(resolution_known_at=prediction.known_as_of)
    with pytest.raises(ValueError, match="resolution was known"):
        evaluate_prediction(prediction, bundle)


def test_evaluator_rejects_market_identity_mismatch() -> None:
    prediction = _prediction()
    payload_bundle = _bundle()
    object.__setattr__(payload_bundle, "market_id", "different")
    with pytest.raises(ValueError, match="market_id"):
        select_eligible_tape_point(prediction, payload_bundle)


def test_evaluator_rejects_no_pre_cutoff_observation() -> None:
    prediction = _prediction(cutoff="2023-11-13T00:00:00Z")
    with pytest.raises(ValueError, match="no eligible market observation"):
        evaluate_prediction(prediction, _bundle())
