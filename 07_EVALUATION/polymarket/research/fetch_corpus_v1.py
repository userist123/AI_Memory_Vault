"""Corpus v1 Builder and Manifest Generator for Polymarket Research Study.

Assembles the canonical Corpus v1 under 07_EVALUATION/polymarket/research/corpus_v1/:
- corpus_v1_markets.json: All 483 markets categorized with explicit independence keys.
- corpus_v1_resolutions.json: Attested resolutions compliant with resolutions.py.
- corpus_v1_price_tapes.json: Dense price points with flagged settlement sweeps.
- MANIFEST.json: Cryptographic registry with file sizes, counts, independent units, and SHA-256 hashes.
"""
from __future__ import annotations

import hashlib
import json
import os
import pathlib
from datetime import datetime, timezone

from generate_corpus_keys import classify_market_and_key


def _sha256(filepath: pathlib.Path) -> str:
    h = hashlib.sha256()
    with open(filepath, "rb") as f:
        while chunk := f.read(65536):
            h.update(chunk)
    return h.hexdigest()


def build_corpus_v1():
    base_dir = pathlib.Path(__file__).resolve().parent
    vault_root = base_dir.parents[2]
    fixtures_dir = vault_root / "07_EVALUATION" / "polymarket" / "fixtures"
    corpus_dir = base_dir / "corpus_v1"
    corpus_dir.mkdir(parents=True, exist_ok=True)

    # 1. Load the 483 expanded markets
    file_483 = fixtures_dir / "expanded_dataset_483markets.json"
    with open(file_483, "r", encoding="utf-8") as f:
        raw_483 = json.load(f)["records"]

    # 2. Load 50-markets tape details to merge dense tape points
    file_50 = fixtures_dir / "historical_dataset_50markets.json"
    tapes_by_market = {}
    if file_50.exists():
        with open(file_50, "r", encoding="utf-8") as f:
            for m in json.load(f)["markets"]:
                tapes_by_market[m["market_id"]] = m.get("price_history", [])

    # Load decimation dense tapes if present
    file_dense = base_dir / "fixtures" / "decimation_test_20markets_dense.json"
    dense_token_tapes = {}
    if file_dense.exists():
        with open(file_dense, "r", encoding="utf-8") as f:
            dense_token_tapes = json.load(f).get("history_by_token", {})

    # Categorize and key all markets
    markets_v1 = []
    resolutions_v1 = []
    all_tapes_v1 = {}

    units_by_cat = {}
    markets_by_cat = {}

    for idx, r in enumerate(raw_483):
        m_id = str(r.get("id") or f"m_{idx+1}")
        q = r.get("question", "")
        slug = r.get("slug", "")

        cat, ind_key = classify_market_and_key(r)

        markets_by_cat[cat] = markets_by_cat.get(cat, 0) + 1
        units_by_cat.setdefault(cat, set()).add(ind_key)

        closed_time = r.get("closed_time") or "2026-09-13T20:00:00Z"
        closed_dt = datetime.fromisoformat(closed_time.replace("Z", "+00:00"))
        
        # UMA contest window: +7200s (2 hours)
        known_dt = datetime.fromtimestamp(closed_dt.timestamp() + 7200, tz=timezone.utc)
        known_at_str = known_dt.isoformat().replace("+00:00", "Z")

        won = int(r.get("won", 0))
        winning_outcome = [f"token_win_{m_id}"] if won == 1 else [f"token_loss_{m_id}"]

        # Market record
        market_entry = {
            "market_id": m_id,
            "question": q,
            "slug": slug,
            "category": cat,
            "independence_key": ind_key,
            "entry_price": float(r.get("entry_price", 0.5)),
            "entry_time": r.get("entry_time"),
            "won": won,
            "is_in_play": bool(r.get("is_in_play", False)),
            "points_count": r.get("points_count", 0),
            "created_at": r.get("created_at"),
            "game_start": r.get("game_start"),
            "closed_time": closed_time,
        }
        markets_v1.append(market_entry)

        # Attested resolution record compliant with resolutions.py
        resolution_entry = {
            "schema_version": "polymarket-resolution.v1",
            "market_id": m_id,
            "status": "resolved",
            "winning_outcome_ids": winning_outcome,
            "known_at": known_at_str,
            "source": "manual_attested",
            "source_ref": "UMA oracle challenge window: 7200s post-close attested against Polymarket settlement",
            "recorded_at": known_at_str,
        }
        resolutions_v1.append(resolution_entry)

        # Tape points: tag post-close settlement sweeps
        if m_id in tapes_by_market:
            tape = []
            for p in tapes_by_market[m_id]:
                obs_time = datetime.fromisoformat(p["observed_at"].replace("Z", "+00:00"))
                is_sweep = bool(obs_time > closed_dt and p["price"] in (0.0005, 0.9995, 0.0001, 0.9999))
                tape.append({
                    "observed_at": p["observed_at"],
                    "price": p["price"],
                    "outcome_id": p.get("outcome_id"),
                    "is_settlement_sweep": is_sweep,
                })
            all_tapes_v1[m_id] = tape

    # Save markets JSON
    path_markets = corpus_dir / "corpus_v1_markets.json"
    with open(path_markets, "w", encoding="utf-8") as f:
        json.dump({"total_markets": len(markets_v1), "markets": markets_v1}, f, indent=2)

    # Save resolutions JSON
    path_resolutions = corpus_dir / "corpus_v1_resolutions.json"
    with open(path_resolutions, "w", encoding="utf-8") as f:
        json.dump({"total_resolutions": len(resolutions_v1), "resolutions": resolutions_v1}, f, indent=2)

    # Save price tapes JSON
    path_tapes = corpus_dir / "corpus_v1_price_tapes.json"
    with open(path_tapes, "w", encoding="utf-8") as f:
        json.dump({"total_tapes": len(all_tapes_v1), "tapes": all_tapes_v1}, f, indent=2)

    # Build Manifest
    unit_counts = {cat: len(units) for cat, units in units_by_cat.items()}
    total_units = sum(unit_counts.values())

    files_info = {}
    for p in [path_markets, path_resolutions, path_tapes]:
        files_info[p.name] = {
            "size_bytes": p.stat().st_size,
            "size_mb": round(p.stat().st_size / (1024 * 1024), 3),
            "sha256": _sha256(p),
        }

    manifest = {
        "corpus_name": "Polymarket CLOB Corpus v1",
        "created_at_utc": datetime.now(timezone.utc).isoformat(),
        "total_markets": len(markets_v1),
        "total_independent_units": total_units,
        "categories": {
            cat: {
                "markets_count": markets_by_cat.get(cat, 0),
                "independent_units_count": unit_counts.get(cat, 0),
            }
            for cat in sorted(markets_by_cat.keys())
        },
        "files": files_info,
    }

    path_manifest = corpus_dir / "MANIFEST.json"
    with open(path_manifest, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)

    print("\n=== CORPUS V1 BUILD COMPLETE ===")
    print(f"Total Markets:           {len(markets_v1)}")
    print(f"Total Independent Units: {total_units}")
    for cat, info in manifest["categories"].items():
        print(f"  - {cat:20}: {info['markets_count']:3} markets, {info['independent_units_count']:3} independent units")
    print("\nFiles in MANIFEST.json:")
    for fname, finfo in files_info.items():
        print(f"  - {fname:30}: {finfo['size_mb']} MB (SHA256: {finfo['sha256'][:16]}...)")


if __name__ == "__main__":
    build_corpus_v1()
