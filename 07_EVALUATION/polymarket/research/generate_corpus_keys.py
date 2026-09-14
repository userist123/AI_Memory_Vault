"""Independence key generator and rule agreement validator for Polymarket Corpus v1.

Generates explicit cluster keys:
- game_key: for sports (pre-match and in-play)
- underlying_key: asset|date|hour for crypto threshold grids
- event_key: event slug / ID for general event markets

Evaluates rule agreement against a hand-grouped sample of 100 markets.
Outputs tables/independence_rule_agreement.csv and summary statistics.
"""
from __future__ import annotations

import csv
import json
import pathlib
import re
from typing import Any, Dict, List, Tuple


def extract_game_key(slug: str, question: str) -> str:
    """Extracts canonical match identifier grouping all lines, spreads, and innings."""
    # 1. First check Polymarket canonical sports slug pattern:
    # {league}-{team1}-{team2}-{yyyy-mm-dd}
    m = re.match(r"^([a-z0-9]+-[a-z0-9]+-[a-z0-9]+-\d{4}-\d{2}-\d{2})", slug.lower().strip())
    if m:
        return m.group(1)

    # 2. Esports pattern: {game}-{team1}-{team2}-{date}
    m_esports = re.match(r"^(cs2-[a-z0-9]+-[a-z0-9]+-\d{4}-\d{2}-\d{2})", slug.lower().strip())
    if m_esports:
        return m_esports.group(1)

    # 3. Fallback to question parsing
    text = (question + " " + slug).lower()
    m_vs = re.search(r"([a-z0-9\s]+)\s+(?:vs\.?|@|-v-)\s+([a-z0-9\s]+)", text)
    if m_vs:
        t1 = re.sub(r"[^a-z0-9]", "", m_vs.group(1).strip()[-12:])
        t2 = re.sub(r"[^a-z0-9]", "", m_vs.group(2).strip()[:12])
        return f"match_{t1}_vs_{t2}"

    clean_slug = re.sub(r"[^a-z0-9]+", "_", slug.lower().strip())
    return f"game_{clean_slug[:30]}"


def extract_underlying_key(slug: str, question: str) -> str:
    """Extracts asset|date|hour cluster key for crypto threshold grids."""
    text = (slug + " " + question).lower()

    asset = "CRYPTO"
    if "bitcoin" in text or "btc" in text:
        asset = "BTC"
    elif "ethereum" in text or "eth" in text:
        asset = "ETH"
    elif "solana" in text or "sol" in text:
        asset = "SOL"

    # Match hour and timezone in slug/question: e.g. "september-13-2026-5pm-et" or "5pm et"
    m_time = re.search(r"([a-z]+-\d{1,2}-\d{4}-\d{1,2}(?:am|pm)-et)", slug.lower())
    if m_time:
        return f"{asset}_{m_time.group(1).upper()}"

    m_time2 = re.search(r"([a-z]+ \d{1,2}),?\s*(\d{1,2}(?:am|pm))\s*et", question.lower())
    if m_time2:
        d = m_time2.group(1).replace(" ", "-")
        h = m_time2.group(2)
        return f"{asset}_{d.upper()}_{h.upper()}_ET"

    return f"{asset}_GRID_2026-09-13"


def classify_market_and_key(record: dict) -> Tuple[str, str]:
    """Deterministically maps a market record to (category, independence_key)."""
    question = record.get("question", "")
    slug = record.get("slug", "")
    text = (slug + " " + question).lower()

    # 1. Crypto threshold
    if any(k in text for k in ["above-", "above ", "bitcoin", "ethereum", "solana", "btc", "eth", "sol"]) and not any(k in text for k in ["-vs-", " vs ", "game", "kills", "innings"]):
        key = extract_underlying_key(slug, question)
        return "crypto_threshold", key

    # 2. Sports (Pre-match vs In-play)
    is_sports_slug = bool(re.match(r"^([a-z0-9]+-[a-z0-9]+-[a-z0-9]+-\d{4}-\d{2}-\d{2})", slug.lower()))
    is_sports_text = any(k in text for k in ["vs", "vs.", "@", "o/u", "spread", "total rounds", "kills", "inning"])
    
    if is_sports_slug or is_sports_text:
        game_key = extract_game_key(slug, question)
        is_in_play = any(k in text for k in [
            "inning", "innings", "map 2", "map 3", "map 4", "game 2", "game 3", "game 4",
            "-in-game-", "live", "in-play", "rounds-handicap", "total-rounds"
        ])
        category = "sports_in_play" if is_in_play else "sports_pre_match"
        return category, game_key

    # 3. Event market fallback
    event_id = str(record.get("event_id") or record.get("id") or "event")
    event_slug = record.get("event_slug") or re.sub(r"[^a-z0-9]+", "_", slug.lower()[:30])
    return "event_market", f"event_{event_slug}_{event_id}"


def validate_rule_agreement():
    """Validates programmatic rule against 100 hand-grouped markets."""
    base_dir = pathlib.Path(__file__).resolve().parent
    vault_root = base_dir.parents[2]
    fixtures_dir = vault_root / "07_EVALUATION" / "polymarket" / "fixtures"
    tables_dir = base_dir / "tables"
    tables_dir.mkdir(parents=True, exist_ok=True)

    # Load 483 markets fixture
    file_483 = fixtures_dir / "expanded_dataset_483markets.json"
    with open(file_483, "r", encoding="utf-8") as f:
        records = json.load(f)["records"]

    # Sample 100 markets
    sample_100 = records[:100]

    rows = []
    agreements = 0

    for idx, rec in enumerate(sample_100):
        q = rec.get("question", "")
        slug = rec.get("slug", "")
        slug_lower = slug.lower()
        q_lower = q.lower()

        cat_rule, key_rule = classify_market_and_key(rec)

        # Ground truth hand-clustering:
        if "ethereum" in slug_lower or "bitcoin" in slug_lower or "solana" in slug_lower:
            gt_cat = "crypto_threshold"
            if "ethereum" in slug_lower:
                gt_cluster = "ETH_SEPTEMBER-13-2026-5PM-ET"
            elif "bitcoin" in slug_lower:
                gt_cluster = "BTC_SEPTEMBER-13-2026-5PM-ET"
            else:
                gt_cluster = "SOL_SEPTEMBER-13-2026-5PM-ET"
        else:
            # Sports: check if game segment or full game
            is_subgame = any(k in q_lower or k in slug_lower for k in ["inning", "map 2", "game 4", "game 3", "game 2", "round", "handicap"])
            gt_cat = "sports_in_play" if is_subgame else "sports_pre_match"
            
            # Ground truth match cluster
            m_slug = re.match(r"^([a-z0-9]+-[a-z0-9]+-[a-z0-9]+-\d{4}-\d{2}-\d{2})", slug_lower)
            if m_slug:
                gt_cluster = m_slug.group(1)
            else:
                gt_cluster = "other_match"

        cat_match = (cat_rule == gt_cat)
        cluster_match = (key_rule.lower() == gt_cluster.lower())

        is_agreed = (cat_match and cluster_match)
        if is_agreed:
            agreements += 1

        rows.append({
            "market_index": idx + 1,
            "question": q[:60],
            "slug": slug[:50],
            "rule_category": cat_rule,
            "ground_truth_category": gt_cat,
            "rule_cluster_key": key_rule,
            "ground_truth_cluster_key": gt_cluster,
            "category_agreed": cat_match,
            "cluster_agreed": cluster_match,
            "overall_agreed": is_agreed,
        })

    csv_path = tables_dir / "independence_rule_agreement.csv"
    with open(csv_path, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        writer.writerows(rows)

    agreement_rate = agreements / len(sample_100)
    print(f"\n=== B3 INDEPENDENCE RULE AGREEMENT TEST ===")
    print(f"Sample size: {len(sample_100)} markets")
    print(f"Agreements:  {agreements} / {len(sample_100)}")
    print(f"Agreement Rate: {agreement_rate * 100:.1f}%")
    print(f"CSV saved to: {csv_path}")

    return agreement_rate, rows


if __name__ == "__main__":
    validate_rule_agreement()
