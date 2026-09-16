"""Fetch script for B2 Decimation Test across 20 identical Polymarket CLOB markets.

Compares interval="all" vs explicit startTs/endTs dense tape queries on identical tokens.
Saves raw responses as offline fixtures in 07_EVALUATION/polymarket/research/fixtures/.
"""
from __future__ import annotations

import json
import os
import pathlib
import socket
import time
import urllib.error
import urllib.request
from datetime import datetime, timezone

# DNS patch for environments where polymarket.com fails local lookup
_orig_getaddrinfo = socket.getaddrinfo


#: Exact hostnames only. A substring test would also reroute any host that
#: merely contains the string, e.g. "polymarket.com.attacker.example".
_POLYMARKET_HOSTS = frozenset({"gamma-api.polymarket.com", "clob.polymarket.com", "polymarket.com"})


def _patched_getaddrinfo(host, port, *args, **kwargs):
    if isinstance(host, str) and host.lower().rstrip(".") in _POLYMARKET_HOSTS:
        return _orig_getaddrinfo("172.64.153.51", port, *args, **kwargs)
    return _orig_getaddrinfo(host, port, *args, **kwargs)


socket.getaddrinfo = _patched_getaddrinfo

CLOB_BASE = "https://clob.polymarket.com"
HEADERS = {"User-Agent": "Antigravity-Research/1.0 (Polymarket CLOB Corpus Study)"}


def _http_get_json(url: str, pause_sec: float = 0.25) -> dict | list:
    time.sleep(pause_sec)
    req = urllib.request.Request(url, headers=HEADERS)
    try:
        with urllib.request.urlopen(req, timeout=15) as resp:
            data = resp.read().decode("utf-8")
            return json.loads(data)
    except urllib.error.HTTPError as e:
        if e.code == 429:
            print(f"[FATAL 429] Rate limit encountered at {url}. Aborting immediately.")
            raise
        raise


def run_decimation_fetch():
    vault_root = pathlib.Path(__file__).resolve().parents[3]
    fixture_50 = (
        vault_root
        / "07_EVALUATION"
        / "polymarket"
        / "fixtures"
        / "historical_dataset_50markets.json"
    )
    if not fixture_50.exists():
        raise FileNotFoundError(f"Fixture not found: {fixture_50}")

    with open(fixture_50, "r", encoding="utf-8") as f:
        data_50 = json.load(f)

    # Select 20 markets that have recorded price history
    markets_20 = [m for m in data_50["markets"] if len(m.get("price_history", [])) >= 4][:20]
    if len(markets_20) < 20:
        markets_20 = data_50["markets"][:20]

    out_dir = vault_root / "07_EVALUATION" / "polymarket" / "research" / "fixtures"
    out_dir.mkdir(parents=True, exist_ok=True)

    results_all = {}
    results_dense = {}
    provenance = {
        "created_at": datetime.now(timezone.utc).isoformat(),
        "markets_tested": len(markets_20),
        "endpoint": f"{CLOB_BASE}/prices-history",
        "queries": {},
    }

    print(f"Fetching decimation comparison for {len(markets_20)} markets...")

    for idx, m in enumerate(markets_20):
        market_id = m["market_id"]
        outcome_ids = m["outcome_ids"]
        question = m["question"]
        res_time = datetime.fromisoformat(m["resolution_known_at"].replace("Z", "+00:00"))
        end_ts = int(res_time.timestamp()) + 300  # +5m after close to catch settlement sweeps
        # Use 24h prior to resolution
        start_ts = end_ts - 86400

        print(f"[{idx+1}/{len(markets_20)}] Market {market_id}: {question[:45]}...")

        for token in outcome_ids:
            # 1. Fetch interval=all
            url_all = f"{CLOB_BASE}/prices-history?market={token}&interval=all"
            try:
                data_all = _http_get_json(url_all)
                results_all[token] = data_all.get("history", [])
            except Exception as e:
                print(f"  [ERROR] interval=all for {token[:12]}: {e}")
                results_all[token] = []

            # 2. Fetch dense (startTs/endTs window)
            url_dense = f"{CLOB_BASE}/prices-history?market={token}&startTs={start_ts}&endTs={end_ts}"
            try:
                data_dense = _http_get_json(url_dense)
                pts_dense = data_dense.get("history", [])
                # If startTs window returned empty (e.g. if market closed earlier or later), fallback to interval=1d
                if not pts_dense:
                    url_fallback = f"{CLOB_BASE}/prices-history?market={token}&interval=1d"
                    data_fallback = _http_get_json(url_fallback)
                    pts_dense = data_fallback.get("history", [])
                results_dense[token] = pts_dense
            except Exception as e:
                print(f"  [ERROR] dense query for {token[:12]}: {e}")
                results_dense[token] = []

            provenance["queries"][token] = {
                "market_id": market_id,
                "url_all": url_all,
                "url_dense": url_dense,
                "count_all": len(results_all[token]),
                "count_dense": len(results_dense[token]),
            }
            print(f"  Token {token[:16]}...: all={len(results_all[token])} pts, dense={len(results_dense[token])} pts")

    # Save raw fixtures
    file_all = out_dir / "decimation_test_20markets_all.json"
    file_dense = out_dir / "decimation_test_20markets_dense.json"
    file_prov = out_dir / "decimation_test_20markets.provenance.json"

    with open(file_all, "w", encoding="utf-8") as f:
        json.dump({"history_by_token": results_all}, f, indent=2)

    with open(file_dense, "w", encoding="utf-8") as f:
        json.dump({"history_by_token": results_dense}, f, indent=2)

    with open(file_prov, "w", encoding="utf-8") as f:
        json.dump(provenance, f, indent=2)

    print(f"\n[DONE] Saved fixtures to {out_dir}")
    print(f"  - {file_all.name}")
    print(f"  - {file_dense.name}")
    print(f"  - {file_prov.name}")


if __name__ == "__main__":
    run_decimation_fetch()
