from __future__ import annotations

from dataclasses import replace
import json
import pytest

from packages.polymarket.historical_paper_replay import build_market_bundle
from packages.polymarket.model_historical_evaluator_v2 import evaluate_prediction, select_eligible_tape_point
from packages.polymarket.prediction_ledger import PredictionProvenance, build_prediction


def _prov():
    return PredictionProvenance("evidence", ("snapshot-1",), ("source-1",), ("test",), "test-model", "1")


def _prediction(cutoff="2023-11-15T12:00:00Z"):
    return build_prediction(market_id="market-1", outcome_id="token-yes", probability=0.75, predicted_at=cutoff, known_as_of=cutoff, provenance=_prov())


def _bundle(resolution_known_at="2026-01-01T00:00:00Z", with_provenance=True):
    payload = {"id":"market-1","question":"Will A happen?","closed":True,"outcomes":json.dumps(["Yes","No"]),"clobTokenIds":json.dumps(["token-yes","token-no"]),"outcomePrices":json.dumps(["1","0"])}
    history = {"token-yes":[{"t":1700000000,"p":0.40},{"t":1700086400,"p":0.60}],"token-no":[{"t":1700000000,"p":0.60},{"t":1700086400,"p":0.40}]}
    bundle = build_market_bundle(payload, resolution_known_at=resolution_known_at, history_by_token=history)
    if with_provenance:
        at = "2023-11-15T01:00:00Z"
        bundle = replace(bundle, price_history=tuple(replace(p, known_as_of=at, acquired_at=at) for p in bundle.price_history))
    return bundle


def test_selects_latest_eligible_point():
    result = evaluate_prediction(_prediction(), _bundle())
    assert result.selected_observed_at == "2023-11-14T22:13:20Z"
    assert result.market_price == pytest.approx(0.40)
    assert result.edge == pytest.approx(0.35)
    assert result.settlement_win is True
    assert result.settlement_units_per_notional == pytest.approx(2.5)


def test_rejects_resolution_known_at_cutoff():
    prediction = _prediction()
    with pytest.raises(ValueError, match="resolution was known"):
        evaluate_prediction(prediction, _bundle(resolution_known_at=prediction.known_as_of))


def test_rejects_market_mismatch():
    prediction = _prediction()
    bundle = _bundle()
    object.__setattr__(bundle, "market_id", "different")
    with pytest.raises(ValueError, match="market_id"):
        select_eligible_tape_point(prediction, bundle)


def test_rejects_no_pre_cutoff_point():
    with pytest.raises(ValueError, match="no eligible market observation"):
        evaluate_prediction(_prediction("2023-11-13T00:00:00Z"), _bundle())


def test_rejects_missing_explicit_provenance():
    with pytest.raises(ValueError, match="explicit temporal provenance"):
        evaluate_prediction(_prediction(), _bundle(with_provenance=False))
