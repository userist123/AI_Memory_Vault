"""Temporal leakage defences, exercised on captured Polymarket data.

Every other test in this package runs on fixtures we wrote. This one runs on
`07_EVALUATION/polymarket/fixtures/historical_dataset_50markets.json`: 50 real
resolved markets and 620 real price points, fetched from the public API.

The repository carries two replay paths, and they do not defend the same
things.

`historical_replay.HistoricalReplay` requires `acquired_at` and `known_as_of`
on every price point, refuses an observation dated after it became known, and
admits a point to a replay only when all three timestamps precede the cutoff.

`historical_paper_replay`, written later for real data, keeps the fields but
enforces none of it. The tests marked `xfail(strict=True)` below describe what
that path lets through. They fail today because the defect is present; the day
someone repairs it they pass unexpectedly, strict mode turns that into a
failure, and the marker has to come off. The fix is not made here: which
timestamp an observation should carry is a decision, not a refactor.
"""
from __future__ import annotations

import json
import pathlib
from datetime import datetime

import pytest

from packages.polymarket.historical_paper_replay import (
    HistoricalMarketBundle,
    HistoricalTapePoint,
    build_market_bundle,
    run_mechanical_control,
)
from packages.polymarket.historical_replay import HistoricalPricePoint

_FIXTURE = (
    pathlib.Path(__file__).resolve().parents[2]
    / "07_EVALUATION" / "polymarket" / "fixtures" / "historical_dataset_50markets.json"
)


def _ts(value: str) -> datetime:
    return datetime.fromisoformat(value.replace("Z", "+00:00"))


@pytest.fixture(scope="module")
def markets() -> list[dict]:
    if not _FIXTURE.exists():
        pytest.skip("real-data fixture absent from this checkout")
    payload = json.loads(_FIXTURE.read_text(encoding="utf-8"))
    assert payload["market_count"] == 50
    assert payload["total_price_points"] == 620
    return payload["markets"]


def _bundle(record: dict) -> HistoricalMarketBundle:
    return HistoricalMarketBundle(
        market_id=record["market_id"],
        question=record["question"],
        outcome_ids=tuple(record["outcome_ids"]),
        outcomes=tuple(record["outcomes"]),
        resolution_outcome_ids=tuple(record["resolution_outcome_ids"]),
        resolution_known_at=record["resolution_known_at"],
        price_history=tuple(HistoricalTapePoint(**p) for p in record["price_history"]),
        metadata_source_ref=record["metadata_source_ref"],
    )


# --- the fixture itself -------------------------------------------------------

def test_the_fixture_has_the_shape_the_findings_describe(markets):
    """Pinned so a regenerated fixture cannot silently change what the xfail
    tests below are asserting about."""
    points = [p for m in markets for p in m["price_history"]]
    after = [
        p for m in markets for p in m["price_history"]
        if _ts(p["observed_at"]) > _ts(m["resolution_known_at"])
    ]
    assert len(points) == 620
    assert all(p["known_as_of"] is None for p in points)
    assert len(after) == 22
    assert {p["price"] for p in after} == {0.0005, 0.9995}


# --- defences that hold -------------------------------------------------------

def test_the_older_replay_refuses_an_observation_dated_after_it_was_known(markets):
    """`HistoricalPricePoint` will not accept a price observed after the moment
    it claims to have become known — the ordering the newer path never checks."""
    point = markets[0]["price_history"][0]
    with pytest.raises(ValueError, match="observed after it is known"):
        HistoricalPricePoint(
            outcome_id=point["outcome_id"],
            observed_at=point["observed_at"],
            price=str(point["price"]),
            requested_fidelity_minutes=None,
            source_type="clob_prices_history",
            source_ref=point["source_ref"],
            acquired_at=point["observed_at"],
            known_as_of="2020-01-01T00:00:00Z",
        ).validate()


def test_the_older_replay_will_not_take_a_point_without_a_knowledge_time(markets):
    """All 620 captured points carry `known_as_of: null`, correctly — there was
    nothing honest to put there. The older path cannot even construct a point
    from them, which is the fail-closed behaviour."""
    point = markets[0]["price_history"][0]
    with pytest.raises(ValueError, match="known_as_of must be a non-empty ISO-8601 string"):
        HistoricalPricePoint(
            outcome_id=point["outcome_id"],
            observed_at=point["observed_at"],
            price=str(point["price"]),
            requested_fidelity_minutes=None,
            source_type="clob_prices_history",
            source_ref=point["source_ref"],
            acquired_at=point["observed_at"],
            known_as_of=point["known_as_of"],
        ).validate()


# --- breaches in the newer path ----------------------------------------------

@pytest.mark.xfail(
    strict=True,
    reason="HistoricalMarketBundle.validate never compares observed_at against "
           "resolution_known_at; 22 captured points dated after resolution pass",
)
def test_a_bundle_refuses_prices_observed_after_its_own_resolution(markets):
    """The 22 points are the settlement sweep — prices of exactly 0.0005 and
    0.9995, 30 to 66 seconds after close. None is the first quote of its
    market, so the mechanical control was not contaminated by them. A bundle
    that accepts them is still a bundle that will accept a real post-resolution
    quote the day one appears."""
    offending = [
        m for m in markets
        if any(_ts(p["observed_at"]) > _ts(m["resolution_known_at"]) for p in m["price_history"])
    ]
    assert offending, "fixture must contain post-resolution points"
    refused = 0
    for record in offending:
        try:
            _bundle(record).validate()
        except ValueError:
            refused += 1
    assert refused == len(offending)


@pytest.mark.xfail(
    strict=True,
    reason="run_mechanical_control trades on a tape point whose known_as_of is "
           "None instead of refusing an observation of unknown availability",
)
def test_the_replay_refuses_to_trade_on_a_point_of_unknown_availability(markets):
    """Every captured point has `known_as_of` null. The older replay path
    cannot construct such a point; this one executes a trade on it."""
    refused = 0
    for record in markets:
        try:
            run_mechanical_control(_bundle(record))
        except ValueError:
            refused += 1
    assert refused == len(markets)


@pytest.mark.xfail(
    strict=True,
    reason="build_market_bundle stamps acquired_at and known_as_of on every "
           "price point with resolution_known_at, so a quote's availability "
           "moves with the resolution date",
)
def test_when_a_price_became_known_does_not_depend_on_when_the_market_resolved(markets):
    """Stated without choosing the right value. Whatever moment a quote became
    public, it cannot depend on when the market later settled; in production
    `collect_resolved_bundles` passes `datetime.now()`, so the moment the script
    ran is written onto quotes observed hours earlier.

    Two bundles built from the same payload and the same tape, differing only
    in the resolution date, must give every price point the same knowledge
    time."""
    record = next(m for m in markets if len(m["price_history"]) >= 2)
    payload = {
        "id": record["market_id"],
        "question": record["question"],
        "closed": True,
        "outcomes": json.dumps(record["outcomes"]),
        "clobTokenIds": json.dumps(record["outcome_ids"]),
        "outcomePrices": json.dumps(
            ["1" if o in record["resolution_outcome_ids"] else "0" for o in record["outcome_ids"]]
        ),
    }
    history = {}
    for p in record["price_history"]:
        history.setdefault(p["outcome_id"], []).append(
            {"t": _ts(p["observed_at"]).timestamp(), "p": p["price"]}
        )

    early = build_market_bundle(payload, resolution_known_at="2026-09-13T21:00:00Z", history_by_token=history)
    late = build_market_bundle(payload, resolution_known_at="2026-12-31T23:59:59Z", history_by_token=history)

    assert [p.known_as_of for p in early.price_history] == [p.known_as_of for p in late.price_history]


@pytest.mark.xfail(
    strict=True,
    reason="HistoricalMarketBundle contains no sister-market timeline isolation: "
           "an outcome resolved on an earlier line or inning can be consumed by "
           "concurrent market replay on the same event without cross-line temporal gating",
)
def test_sister_market_resolution_cannot_leak_across_concurrent_lines(markets):
    """Sister markets (e.g. lines on the same game or strikes on the same asset)
    frequently resolve at different moments. Without cross-market dependency tracking,
    the resolution of an early sister market can leak into the prediction of a later
    sister market on the same event."""
    sister_a = markets[1]
    sister_b = markets[2]
    bundle_a = _bundle(sister_a)
    bundle_b = _bundle(sister_b)
    assert hasattr(bundle_a, "sister_market_cutoffs"), (
        "bundle must enforce sister market resolution cutoffs within shared event clusters"
    )

