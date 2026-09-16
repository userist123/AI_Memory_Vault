"""Fetch live Gamma markets into immutable snapshots.

Everything downstream of here has been written and tested against fixtures.
This is the piece that makes it point at reality, and it is the only module in
the package that touches the network.

## Shape

    fetch_page      the only function that opens a socket, and the only one
                    that cannot be tested offline
    ingest_payload  turns a fetched page into snapshots; pure, takes the page
                    as an argument, and carries every test in this module

The split is the point. A fetch buried inside the parsing would make the whole
path untestable without a network, and an ingestion path nobody can test
offline is one that gets verified by running it against production.

## Timestamps

`acquired_at` is supplied by the caller, never read from a clock inside the
parse. A snapshot's honesty rests on that field, and a function that stamps
its own arrival time will happily stamp it again on a replay of yesterday's
payload — which is how a historical fixture quietly becomes a live
observation.

`known_as_of` equals `acquired_at` here. The Gamma endpoint returns current
state, so the moment it was fetched is the moment it became knowable. A
historical loader would set these differently and must not reuse this function
to do it.

## What this does not do

No credentials — Gamma's market endpoint is public and this sends no
authentication of any kind. No orders, no wallet, no trading. It reads and it
writes JSON files.

It also does not decide anything is *resolved*. `parse_gamma_market` already
refuses to infer historical resolution, and a resolution recorded from a live
listing would be a resolution timestamped at the moment someone happened to
look — useless for backtesting, and worse than useless because it looks usable.
"""
from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable, Iterable, Mapping, Optional, Sequence

from .market_snapshot import (
    MarketLifecycle,
    PolymarketMarket,
    PriceObservation,
    SnapshotStore,
    build_snapshot,
    canonical_json,
    parse_gamma_market,
)

GAMMA_MARKETS_URL = "https://gamma-api.polymarket.com/markets"

SOURCE_TYPE = "gamma-api"
DATA_QUALITY_VERIFIED = "verified"

#: Gamma caps a page well below this; asking for more silently returns fewer,
#: which would look like the end of the data.
MAX_PAGE_LIMIT = 500


@dataclass(frozen=True)
class IngestResult:
    fetched: int
    written: int
    skipped_unparseable: int
    skipped_duplicate: int
    reasons: tuple[str, ...] = ()

    def as_dict(self) -> dict[str, object]:
        return {
            "fetched": self.fetched,
            "written": self.written,
            "skipped_unparseable": self.skipped_unparseable,
            "skipped_duplicate": self.skipped_duplicate,
            "reasons": list(self.reasons),
        }


def fetch_page(
    *,
    limit: int = 100,
    offset: int = 0,
    url: str = GAMMA_MARKETS_URL,
    timeout: float = 30.0,
) -> list[Mapping[str, Any]]:
    """The one function here that opens a socket.

    Deliberately thin: build a URL, read it, parse JSON, return the list. Every
    decision about what the data *means* lives in `ingest_payload`, which takes
    the result as an argument and is therefore testable without a network.

    No authentication is sent. The markets endpoint is public.
    """
    import urllib.parse
    import urllib.request

    if not 0 < limit <= MAX_PAGE_LIMIT:
        raise ValueError(f"limit must be between 1 and {MAX_PAGE_LIMIT}")
    if offset < 0:
        raise ValueError("offset cannot be negative")

    query = urllib.parse.urlencode({"limit": limit, "offset": offset})
    request = urllib.request.Request(
        f"{url}?{query}",
        headers={"Accept": "application/json", "User-Agent": "ai-memory-vault-research"},
    )
    with urllib.request.urlopen(request, timeout=timeout) as response:
        payload = json.loads(response.read().decode("utf-8"))

    if not isinstance(payload, list):
        raise ValueError("Gamma markets endpoint must return a JSON array")
    return payload


def lifecycle_from(parsed: Mapping[str, Any]) -> MarketLifecycle:
    """Gamma's two booleans, mapped without inventing a third state.

    `closed` and `active` say whether trading has stopped, not whether the
    question was answered. A closed market is CLOSED, never RESOLVED — the
    resolution and the timestamp it became knowable come from somewhere else,
    and guessing RESOLVED here would put an unknowable `known_at` into the
    store.
    """
    if parsed.get("closed"):
        return MarketLifecycle.CLOSED
    if parsed.get("active"):
        return MarketLifecycle.OPEN
    return MarketLifecycle.UNKNOWN


def _price_observations(parsed: Mapping[str, Any], observed_at: str) -> tuple[PriceObservation, ...]:
    """Pair outcome ids with prices, or return nothing.

    Gamma returns `outcomes`, `outcomePrices` and `clobTokenIds` as parallel
    arrays. When their lengths disagree the pairing is a guess, and a guessed
    price attached to the wrong outcome is worse than an absent one: it is
    indistinguishable from a real observation downstream.
    """
    ids = parsed.get("outcome_ids") or []
    prices = parsed.get("outcome_prices") or []
    if not ids or len(ids) != len(prices):
        return ()
    return tuple(
        PriceObservation(
            outcome_id=str(outcome_id),
            price=str(price),
            observed_at=observed_at,
            source=SOURCE_TYPE,
        )
        for outcome_id, price in zip(ids, prices)
    )


def ingest_payload(
    markets: Iterable[Mapping[str, Any]],
    *,
    acquired_at: str,
    store: SnapshotStore,
    source_ref: str = GAMMA_MARKETS_URL,
) -> IngestResult:
    """Turn a fetched page into immutable snapshots. No network, no clock.

    `acquired_at` is the caller's. Nothing here reads the time, because a
    function that stamps its own arrival time will stamp it again on a replay
    of yesterday's payload, and a historical fixture silently becomes a live
    observation.

    A market that cannot be parsed is counted and skipped, never repaired.
    Repairing it means inventing a field, and section 41 of the brief forbids
    fabricating Gamma fields for exactly the reason that an invented field is
    indistinguishable from a real one once it is in the store.
    """
    fetched = written = unparseable = duplicate = 0
    reasons: list[str] = []

    for payload in markets:
        fetched += 1
        try:
            parsed = parse_gamma_market(payload)
        except (ValueError, KeyError, TypeError) as exc:
            unparseable += 1
            reasons.append(f"{payload.get('id', '?')}: {exc}")
            continue

        market = PolymarketMarket(
            market_id=parsed["market_id"],
            condition_id=parsed.get("condition_id"),
            question=parsed["question"],
            description=parsed.get("description"),
            category=parsed.get("category"),
            market_type=parsed.get("market_type"),
            outcomes=tuple(parsed.get("outcomes") or ()),
            outcome_ids=tuple(parsed.get("outcome_ids") or ()),
            lifecycle=lifecycle_from(parsed),
            created_at=parsed.get("created_at"),
            start_at=parsed.get("start_at"),
            close_at=parsed.get("close_at"),
            expected_resolution_at=parsed.get("expected_resolution_at"),
            resolution_source=parsed.get("resolution_source"),
            #: Gamma's listing carries no rule text. None rather than the
            #: description, which is marketing copy and would read downstream
            #: as the resolution criteria it is not.
            resolution_rule_text=None,
            cancellation_state=None,
            slug=parsed.get("slug"),
        )

        snapshot = build_snapshot(
            market,
            snapshot_at=acquired_at,
            acquired_at=acquired_at,
            #: Equal to acquired_at: the endpoint returns current state, so the
            #: moment it was fetched is the moment it became knowable.
            known_as_of=acquired_at,
            source_type=SOURCE_TYPE,
            source_ref=source_ref,
            data_quality=DATA_QUALITY_VERIFIED,
            source_payload_hash=hashlib.sha256(
                canonical_json(dict(payload)).encode("utf-8")
            ).hexdigest(),
            price_observations=_price_observations(parsed, acquired_at),
            liquidity=str(parsed["liquidity"]) if parsed.get("liquidity") is not None else None,
            volume=str(parsed["volume"]) if parsed.get("volume") is not None else None,
            #: No resolution, ever, from a live listing. See the module
            #: docstring — a resolution timestamped at the moment someone
            #: happened to look is worse than none.
            resolution=None,
        )

        #: Asked before the write, because the store treats identical bytes as
        #: a silent no-op. Without this, re-running the ingest reports every
        #: market as written and a reader cannot tell a fresh pull from a
        #: repeated one.
        already = (store.root / f"{snapshot.snapshot_id}.json").exists()
        try:
            store.put(snapshot)
        except ValueError as exc:
            #: Content-addressed: identical bytes are a no-op, different bytes
            #: under one id is a collision and must not be swallowed.
            if "immutable snapshot collision" not in str(exc):
                raise
            duplicate += 1
            reasons.append(f"{parsed['market_id']}: {exc}")
            continue
        if already:
            duplicate += 1
        else:
            written += 1

    return IngestResult(fetched, written, unparseable, duplicate, tuple(reasons))


def main(argv: Optional[Sequence[str]] = None) -> int:
    import argparse
    import datetime

    ap = argparse.ArgumentParser(
        description="Fetch Gamma markets into immutable snapshots. Read-only; "
                    "sends no credentials and places no orders.",
    )
    ap.add_argument("--out", required=True, type=Path, help="snapshot store directory")
    ap.add_argument("--limit", type=int, default=100)
    ap.add_argument("--pages", type=int, default=1)
    ap.add_argument("--url", default=GAMMA_MARKETS_URL)
    ap.add_argument("--dry-run", action="store_true",
                    help="fetch and report, write nothing")
    args = ap.parse_args(argv)

    #: Read once, at the top, and passed down explicitly. The one place a clock
    #: is allowed in this path.
    acquired_at = datetime.datetime.now(datetime.timezone.utc).strftime(
        "%Y-%m-%dT%H:%M:%SZ"
    )
    store = SnapshotStore(args.out if not args.dry_run else Path(args.out))

    totals = IngestResult(0, 0, 0, 0)
    for page in range(args.pages):
        markets = fetch_page(limit=args.limit, offset=page * args.limit, url=args.url)
        if not markets:
            break
        if args.dry_run:
            print(f"page {page}: {len(markets)} markets (dry run, nothing written)")
            totals = IngestResult(totals.fetched + len(markets), 0, 0, 0)
            continue
        result = ingest_payload(markets, acquired_at=acquired_at, store=store,
                               source_ref=args.url)
        totals = IngestResult(
            totals.fetched + result.fetched,
            totals.written + result.written,
            totals.skipped_unparseable + result.skipped_unparseable,
            totals.skipped_duplicate + result.skipped_duplicate,
            totals.reasons + result.reasons,
        )

    print(json.dumps(totals.as_dict(), indent=2))
    print(f"\nacquired_at {acquired_at}")
    if totals.skipped_unparseable:
        print(f"{totals.skipped_unparseable} market(s) skipped rather than repaired; "
              "see reasons above")
    return 0


__all__ = [
    "GAMMA_MARKETS_URL",
    "MAX_PAGE_LIMIT",
    "IngestResult",
    "lifecycle_from",
    "fetch_page",
    "ingest_payload",
    "main",
]


if __name__ == "__main__":
    raise SystemExit(main())
