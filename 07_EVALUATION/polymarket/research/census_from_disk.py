"""Reproduces every figure in POPULATION_CENSUS_FROM_DISK.md from committed fixtures.

The census was first computed with inline commands that were never saved. That
is the same defect found in the delivery reports it was checking, so it is
committed here and must reproduce the published numbers exactly.

Offline. Run from the repository root:

    python 07_EVALUATION/polymarket/research/census_from_disk.py
"""
from __future__ import annotations

import collections
import json
import pathlib
import random
import re

FIXTURES = pathlib.Path(__file__).resolve().parents[1] / "fixtures"
BOOTSTRAP_REPLICATES = 4000
BOOTSTRAP_SEED = 7


def _kind(question: str) -> str:
    q = question.lower()
    if re.search(r"o/u|spread|vs\.|vs |win on|map \d|game \d|set \d", q):
        return "sport/esports"
    if re.search(r"bitcoin|ethereum|solana|xrp|btc|eth |price of", q):
        return "crypto price"
    if re.search(r"elect|president|senate|trump|biden|vote|poll|parliament|minister|party|governor", q):
        return "politics"
    if re.search(r"fed |rate|inflation|cpi|gdp|recession|unemploy", q):
        return "macro"
    return "other"


def _underlying(market: dict) -> str:
    """event_key groups markets by game but gives every crypto strike its own
    key; strikes on one asset at one hour resolve from the same price."""
    match = re.match(
        r"(Bitcoin|Ethereum|Solana|XRP) (?:above|below) [\d,.]+ on ([A-Za-z]+ \d+), (\d+(?:AM|PM)) ET",
        market["question"],
    )
    if match:
        return "|".join(match.groups())
    return market["event_key"]


def era_table() -> dict:
    rows = json.loads((FIXTURES / "corpus_depth_sample_500.json").read_text(encoding="utf-8"))
    out = {}
    for label, keep in (("CLOB", True), ("pre-CLOB", False)):
        group = [r for r in rows if (r.get("enableOrderBook") is True) == keep]
        out[label] = {"n": len(group), **collections.Counter(_kind(r.get("question", "")) for r in group)}
    return out


def independence() -> dict:
    records = json.loads(
        (FIXTURES / "expanded_dataset_483markets.json").read_text(encoding="utf-8")
    )["records"]
    clusters = collections.defaultdict(list)
    for market in records:
        clusters[_underlying(market)].append(market)

    def gap(keys):
        markets = [m for k in keys for m in clusters[k]]
        return (sum(m["won"] for m in markets) - sum(float(m["entry_price"]) for m in markets)) / len(markets)

    keys = list(clusters)
    rng = random.Random(BOOTSTRAP_SEED)
    replicates = sorted(
        gap([rng.choice(keys) for _ in keys]) for _ in range(BOOTSTRAP_REPLICATES)
    )
    crypto = sum(1 for k in keys if "|" in k)
    return {
        "markets": len(records),
        "distinct_event_key": len({m["event_key"] for m in records}),
        "independent_outcomes": len(keys),
        "crypto_hourly_fixings": crypto,
        "games": len(keys) - crypto,
        "gap": round(gap(keys), 4),
        "cluster_ci95": [
            round(replicates[int(0.025 * BOOTSTRAP_REPLICATES)], 4),
            round(replicates[int(0.975 * BOOTSTRAP_REPLICATES)], 4),
        ],
    }


if __name__ == "__main__":
    print(json.dumps({"era_table": era_table(), "independence": independence()}, indent=2))
