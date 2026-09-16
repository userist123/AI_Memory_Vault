"""Ingestion, tested without a network.

The whole point of splitting `fetch_page` from `ingest_payload` is that these
run offline. An ingestion path nobody can test without a socket is one that gets
verified against production.

The payloads below use Gamma's real field names and its real quirk of returning
arrays as JSON *strings*. They are shaped like responses, not invented — no
field appears here that `parse_gamma_market` does not already read.
"""
from __future__ import annotations

import json

import pytest

from packages.polymarket.gamma_ingest import (
    GAMMA_MARKETS_URL,
    MAX_PAGE_LIMIT,
    fetch_page,
    ingest_payload,
    lifecycle_from,
)
from packages.polymarket.market_snapshot import MarketLifecycle, SnapshotStore

ACQUIRED = "2026-03-01T12:00:00Z"


def market(**overrides) -> dict:
    base = {
        "id": "512345",
        "conditionId": "0xabc",
        "question": "Will the rate be cut in March?",
        "description": "Resolves per the published decision.",
        "category": "Economics",
        "marketType": "normal",
        #: Gamma returns these as JSON strings, not arrays. The parser handles
        #: both and this fixture uses the awkward one on purpose.
        "outcomes": json.dumps(["Yes", "No"]),
        "outcomePrices": json.dumps(["0.62", "0.38"]),
        "clobTokenIds": json.dumps(["tok-yes", "tok-no"]),
        "active": True,
        "closed": False,
        "createdAt": "2026-01-05T10:00:00Z",
        "startDate": "2026-01-05T10:00:00Z",
        "endDate": "2026-03-20T00:00:00Z",
        "resolutionSource": "Federal Reserve",
        "liquidity": "84000",
        "volume": "120000",
        "slug": "rate-cut-march",
    }
    base.update(overrides)
    return base


@pytest.fixture
def store(tmp_path) -> SnapshotStore:
    return SnapshotStore(tmp_path / "snapshots")


# --- the happy path ----------------------------------------------------------

def test_a_market_becomes_an_immutable_snapshot(store):
    result = ingest_payload([market()], acquired_at=ACQUIRED, store=store)
    assert (result.fetched, result.written) == (1, 1)
    assert result.skipped_unparseable == 0

    written = list(store.root.glob("*.json"))
    assert len(written) == 1
    payload = json.loads(written[0].read_text(encoding="utf-8"))
    assert payload["market"]["question"] == "Will the rate be cut in March?"
    assert payload["acquired_at"] == ACQUIRED
    assert payload["known_as_of"] == ACQUIRED


def test_prices_are_paired_with_their_outcome_ids(store):
    ingest_payload([market()], acquired_at=ACQUIRED, store=store)
    payload = json.loads(next(store.root.glob("*.json")).read_text(encoding="utf-8"))
    prices = {p["outcome_id"]: p["price"] for p in payload["price_observations"]}
    assert prices == {"tok-yes": "0.62", "tok-no": "0.38"}


def test_ingesting_the_same_payload_twice_writes_once(store):
    ingest_payload([market()], acquired_at=ACQUIRED, store=store)
    second = ingest_payload([market()], acquired_at=ACQUIRED, store=store)
    assert second.written == 0
    assert len(list(store.root.glob("*.json"))) == 1


# --- the refusals ------------------------------------------------------------

def test_mismatched_price_and_id_arrays_yield_no_prices(store):
    """A guessed pairing is worse than an absent one: once in the store it is
    indistinguishable from a real observation."""
    ingest_payload(
        [market(outcomePrices=json.dumps(["0.62"]))],
        acquired_at=ACQUIRED, store=store,
    )
    payload = json.loads(next(store.root.glob("*.json")).read_text(encoding="utf-8"))
    assert payload["price_observations"] == []
    assert payload["market"]["question"], "the market is still recorded"


def test_an_unparseable_market_is_counted_and_skipped_not_repaired(store):
    """Repairing it means inventing a field, and an invented field is
    indistinguishable from a real one once it is in the store."""
    result = ingest_payload(
        [market(), {"id": "999"}],  # no question
        acquired_at=ACQUIRED, store=store,
    )
    assert result.fetched == 2
    assert result.written == 1
    assert result.skipped_unparseable == 1
    assert any("999" in reason for reason in result.reasons)


def test_a_closed_market_is_closed_not_resolved():
    """Gamma's booleans say trading stopped, not that the question was
    answered. Guessing RESOLVED would put an unknowable known_at in the store.
    """
    assert lifecycle_from({"closed": True, "active": False}) == MarketLifecycle.CLOSED
    assert lifecycle_from({"closed": False, "active": True}) == MarketLifecycle.OPEN
    assert lifecycle_from({"closed": False, "active": False}) == MarketLifecycle.UNKNOWN


def test_no_resolution_is_ever_recorded_from_a_live_listing(store):
    ingest_payload(
        [market(closed=True, active=False)], acquired_at=ACQUIRED, store=store
    )
    payload = json.loads(next(store.root.glob("*.json")).read_text(encoding="utf-8"))
    assert payload.get("resolution") is None


def test_the_description_is_not_promoted_to_a_resolution_rule(store):
    """It is marketing copy, and downstream it would read as the criteria it
    is not."""
    ingest_payload([market()], acquired_at=ACQUIRED, store=store)
    payload = json.loads(next(store.root.glob("*.json")).read_text(encoding="utf-8"))
    assert payload["market"]["resolution_rule_text"] is None
    assert payload["market"]["description"] == "Resolves per the published decision."


# --- timestamps --------------------------------------------------------------

def test_the_caller_supplies_acquired_at_and_nothing_here_reads_a_clock(store):
    """A function that stamps its own arrival time stamps it again on a replay
    of yesterday's payload, and a historical fixture becomes a live one."""
    ingest_payload([market()], acquired_at="2020-01-01T00:00:00Z", store=store)
    payload = json.loads(next(store.root.glob("*.json")).read_text(encoding="utf-8"))
    assert payload["acquired_at"] == "2020-01-01T00:00:00Z"

    import pathlib
    source = (pathlib.Path(__file__).resolve().parents[2] / "03_IMPLEMENTATION"
              / "packages" / "polymarket" / "gamma_ingest.py").read_text(encoding="utf-8")
    body = source.split("def main(")[0]
    for forbidden in ("datetime.now", "utcnow", "time.time"):
        assert forbidden not in body, (
            f"{forbidden} outside main() would let the parse stamp its own time"
        )


# --- the network boundary ----------------------------------------------------

@pytest.mark.parametrize("limit", [0, -1, MAX_PAGE_LIMIT + 1])
def test_an_impossible_page_limit_is_refused(limit):
    """Above the cap Gamma silently returns fewer, which looks like the end of
    the data rather than a bad request."""
    with pytest.raises(ValueError, match="limit must be"):
        fetch_page(limit=limit)


def test_a_negative_offset_is_refused():
    with pytest.raises(ValueError, match="offset cannot be negative"):
        fetch_page(offset=-1)


def test_the_module_sends_no_credentials():
    """Gamma's market endpoint is public. Anything resembling authentication
    here would be a key committed to the repository."""
    import pathlib
    source = (pathlib.Path(__file__).resolve().parents[2] / "03_IMPLEMENTATION"
              / "packages" / "polymarket" / "gamma_ingest.py").read_text(encoding="utf-8")
    for forbidden in ("Authorization", "Bearer", "api_key", "apiKey",
                      "private_key", "secret"):
        assert forbidden not in source


def test_only_fetch_page_touches_the_network():
    """The split is what makes everything above testable offline."""
    import pathlib
    source = (pathlib.Path(__file__).resolve().parents[2] / "03_IMPLEMENTATION"
              / "packages" / "polymarket" / "gamma_ingest.py").read_text(encoding="utf-8")
    after_fetch = source.split("def lifecycle_from(")[1]
    for forbidden in ("urlopen", "urllib.request", "requests.", "socket"):
        assert forbidden not in after_fetch, (
            f"{forbidden} below fetch_page would make the parse untestable offline"
        )


def test_the_url_is_the_public_markets_endpoint():
    assert GAMMA_MARKETS_URL == "https://gamma-api.polymarket.com/markets"
