"""Fetch script for B1 Event Markets Census across Polymarket CLOB.

Investigates how many independent event markets (politics, macro, business,
science, geopolitics) in the CLOB era possess usable historical price tapes.
Queries Gamma /events by tag and volume, probes CLOB prices-history per token,
and saves full provenance and query parameters.
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

GAMMA_BASE = "https://gamma-api.polymarket.com"
CLOB_BASE = "https://clob.polymarket.com"
HEADERS = {"User-Agent": "Antigravity-Research/1.0 (Polymarket CLOB Census)"}

# Event tags to query
EVENT_TAGS = [
    "politics",
    "elections",
    "us-election",
    "geopolitics",
    "economy",
    "fed",
    "business",
    "tech",
    "ai",
    "science",
    "culture",
    "pop-culture",
    "international-affairs",
]


def _http_get_json(url: str, pause_sec: float = 0.25) -> dict | list:
    time.sleep(pause_sec)
    req = urllib.request.Request(url, headers=HEADERS)
    try:
        with urllib.request.urlopen(req, timeout=15) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        if e.code == 429:
            print(f"[FATAL 429] Rate limit hit at {url}. Aborting.")
            raise
        raise


def run_event_census():
    vault_root = pathlib.Path(__file__).resolve().parents[3]
    out_dir = vault_root / "07_EVALUATION" / "polymarket" / "research" / "fixtures"
    out_dir.mkdir(parents=True, exist_ok=True)

    queries_log = []
    events_found = {}

    print("=== Step 1: Gathering High-Volume and Tagged Closed Events from Gamma ===")

    # 1. High-volume closed events
    url_vol = f"{GAMMA_BASE}/events?closed=true&order=volume&ascending=false&limit=100"
    print(f"Querying top volume closed events: {url_vol}")
    try:
        ev_vol = _http_get_json(url_vol)
        queries_log.append({"url": url_vol, "count": len(ev_vol), "purpose": "top_volume_closed"})
        for ev in ev_vol:
            events_found[str(ev.get("id"))] = ev
    except Exception as e:
        print(f"Error querying top volume: {e}")

    # 2. Tag-specific closed events
    for tag in EVENT_TAGS:
        url_tag = f"{GAMMA_BASE}/events?closed=true&tag_slug={tag}&limit=50"
        print(f"Querying tag '{tag}': {url_tag}")
        try:
            ev_tag = _http_get_json(url_tag)
            queries_log.append({"url": url_tag, "count": len(ev_tag), "tag": tag})
            for ev in ev_tag:
                events_found[str(ev.get("id"))] = ev
        except Exception as e:
            print(f"Error querying tag '{tag}': {e}")

    print(f"Total distinct closed events gathered: {len(events_found)}")

    # Filter out sports and crypto repetitive threshold series
    SPORTS_KEYWORDS = [
        "nba", "mlb", "nfl", "nhl", "premier league", "laliga", "uefa", "serie a",
        "champions league", "tennis", "formula 1", "f1", "cs:go", "dota", "esports",
        "total rounds", "over/under", "spread:", "match winner", "vs.", " vs "
    ]
    CRYPTO_KEYWORDS = [
        "up or down", "price of bitcoin", "price of ethereum", "price of solana",
        "above on", "dip to", "reach by"
    ]

    candidate_events = []
    for ev_id, ev in events_found.items():
        title = (ev.get("title") or "").lower()
        desc = (ev.get("description") or "").lower()
        slug = (ev.get("slug") or "").lower()

        # Check if it looks like sports
        is_sports = any(k in title or k in slug for k in SPORTS_KEYWORDS)
        # Check if it is short-term crypto threshold
        is_crypto_threshold = any(k in title for k in CRYPTO_KEYWORDS) and ("5m" in slug or "15m" in slug or "hourly" in slug)

        if not is_sports and not is_crypto_threshold:
            candidate_events.append(ev)

    print(f"Candidate non-sports, non-crypto event markets: {len(candidate_events)}")

    # Step 2: Probe market details and CLOB prices-history
    print("\n=== Step 2: Probing CLOB Price Tape Availability for Event Markets ===")

    census_records = []
    usable_tape_count = 0
    empty_tape_count = 0

    # Probe candidates up to 100
    for idx, ev in enumerate(candidate_events[:100]):
        ev_id = str(ev.get("id"))
        title = ev.get("title", "")
        slug = ev.get("slug", "")
        markets = ev.get("markets", [])

        if not markets:
            # Fetch event details to get markets
            url_ev_detail = f"{GAMMA_BASE}/events/{ev_id}"
            try:
                ev_detail = _http_get_json(url_ev_detail, pause_sec=0.2)
                markets = ev_detail.get("markets", [])
            except Exception:
                markets = []

        if not markets:
            continue

        print(f"[{idx+1}/{min(100, len(candidate_events))}] Event: {title[:50]} (ID: {ev_id}, {len(markets)} markets)")

        for m in markets[:3]:  # probe up to 3 sub-markets per event
            m_id = str(m.get("id"))
            m_question = m.get("question", "")
            clob_tokens_raw = m.get("clobTokenIds")
            if isinstance(clob_tokens_raw, str):
                try:
                    clob_tokens = json.loads(clob_tokens_raw)
                except Exception:
                    clob_tokens = []
            elif isinstance(clob_tokens_raw, list):
                clob_tokens = clob_tokens_raw
            else:
                clob_tokens = []

            has_clob = bool(m.get("enableOrderBook") is True or clob_tokens)
            pts_count = 0
            tested_token = None

            if clob_tokens:
                tested_token = clob_tokens[0]
                # Test CLOB tape with interval=all
                url_tape = f"{CLOB_BASE}/prices-history?market={tested_token}&interval=all"
                try:
                    tape_res = _http_get_json(url_tape, pause_sec=0.2)
                    pts = tape_res.get("history", [])
                    pts_count = len(pts)
                except Exception as e:
                    pts_count = 0

            is_usable = (pts_count > 0)
            if is_usable:
                usable_tape_count += 1
            else:
                empty_tape_count += 1

            census_records.append({
                "event_id": ev_id,
                "event_title": title,
                "event_slug": slug,
                "market_id": m_id,
                "market_question": m_question,
                "volume": m.get("volume"),
                "closed_time": m.get("closedTime"),
                "has_clob_tokens": bool(clob_tokens),
                "clob_token_sample": tested_token,
                "clob_price_points_all": pts_count,
                "has_usable_clob_tape": is_usable,
            })
            print(f"    Market {m_id}: CLOB={has_clob}, Points={pts_count}, Usable={is_usable}")

    # Summary
    usable_events = {r["event_id"] for r in census_records if r["has_usable_clob_tape"]}
    total_events_probed = {r["event_id"] for r in census_records}

    summary = {
        "census_executed_at": datetime.now(timezone.utc).isoformat(),
        "total_closed_events_gathered": len(events_found),
        "total_candidate_event_markets": len(candidate_events),
        "events_probed": len(total_events_probed),
        "markets_probed": len(census_records),
        "markets_with_usable_clob_tape": usable_tape_count,
        "markets_with_empty_or_no_clob_tape": empty_tape_count,
        "independent_event_units_with_tape": len(usable_events),
        "queries_log": queries_log,
    }

    # Save fixtures
    fixture_census = out_dir / "event_census_results.json"
    with open(fixture_census, "w", encoding="utf-8") as f:
        json.dump({"summary": summary, "records": census_records}, f, indent=2)

    prov_census = out_dir / "event_census_results.provenance.json"
    with open(prov_census, "w", encoding="utf-8") as f:
        json.dump(summary, f, indent=2)

    print("\n=== B1 CENSUS COMPLETE ===")
    print(f"Total events gathered: {len(events_found)}")
    print(f"Events probed:         {len(total_events_probed)}")
    print(f"Markets probed:        {len(census_records)}")
    print(f"Markets with CLOB tape: {usable_tape_count}")
    print(f"Independent Events with CLOB tape: {len(usable_events)}")
    print(f"Saved: {fixture_census}")


if __name__ == "__main__":
    run_event_census()
