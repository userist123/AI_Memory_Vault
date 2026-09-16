from __future__ import annotations

import json

import pytest

from packages.polymarket.historical_paper_replay import (
    CLOB_PRICES_HISTORY_URL,
    HistoricalMarketBundle,
    HistoricalTapePoint,
    build_market_bundle,
    run_mechanical_control,
)


def _payload() -> dict[str, object]:
    return {
        "id": "m-real-1",
        "question": "Will A happen?",
        "closed": True,
        "outcomes": json.dumps(["Yes", "No"]),
        "clobTokenIds": json.dumps(["token-yes", "token-no"]),
        "outcomePrices": json.dumps(["1", "0"]),
    }


def _history() -> dict[str, list[dict[str, float]]]:
    return {
        "token-yes": [{"t": 1700000000, "p": 0.40}, {"t": 1700086400, "p": 0.60}],
        "token-no": [{"t": 1700000000, "p": 0.60}, {"t": 1700086400, "p": 0.40}],
    }


def test_real_provider_shape_is_bound_to_market_identity() -> None:
    bundle = build_market_bundle(_payload(), resolution_known_at="2026-09-12T10:00:00Z", history_by_token=_history())
    assert bundle.market_id == "m-real-1"
    assert bundle.resolution_outcome_ids == ("token-yes",)
    assert all(point.market_id == "m-real-1" for point in bundle.price_history)
    assert all(point.source_ref.startswith(CLOB_PRICES_HISTORY_URL) for point in bundle.price_history)


def test_mechanical_control_does_not_use_resolution_at_entry() -> None:
    bundle = build_market_bundle(_payload(), resolution_known_at="2026-09-12T10:00:00Z", history_by_token=_history())
    result = run_mechanical_control(bundle, notional=1.0)
    assert result.entry_at == "2023-11-14T22:13:20Z"
    assert result.bought_outcome_id == "token-yes"
    assert result.settlement_value == pytest.approx(result.notional / result.entry_price)
    assert result.pnl > 0


def test_resolution_is_current_knowledge_not_a_pre_cutoff_fact() -> None:
    bundle = build_market_bundle(_payload(), resolution_known_at="2026-09-12T10:00:00Z", history_by_token=_history())
    assert bundle.resolution_known_at > max(point.observed_at for point in bundle.price_history)


def test_cross_market_price_point_is_rejected() -> None:
    bad = HistoricalMarketBundle(
        market_id="m1",
        question="q",
        outcome_ids=("yes", "no"),
        outcomes=("Yes", "No"),
        resolution_outcome_ids=("yes",),
        resolution_known_at="2026-09-12T10:00:00Z",
        price_history=(HistoricalTapePoint("m2", "yes", "2026-01-01T00:00:00Z", 0.5, "src"),),
        metadata_source_ref="meta",
    )
    with pytest.raises(ValueError, match="market_id mismatch"):
        bad.validate()


def test_unambiguous_resolution_requires_one_one_and_rest_zero() -> None:
    payload = _payload()
    payload["outcomePrices"] = json.dumps(["0.8", "0.2"])
    with pytest.raises(ValueError, match="unambiguous terminal"):
        build_market_bundle(payload, resolution_known_at="2026-09-12T10:00:00Z", history_by_token=_history())


def test_missing_history_is_refused() -> None:
    with pytest.raises(ValueError, match="no CLOB historical price points"):
        build_market_bundle(_payload(), resolution_known_at="2026-09-12T10:00:00Z", history_by_token={})


def test_empty_question_is_rejected() -> None:
    payload = _payload()
    payload["question"] = ""
    with pytest.raises(ValueError, match="market_id and question"):
        build_market_bundle(payload, resolution_known_at="2026-09-12T10:00:00Z", history_by_token=_history())
