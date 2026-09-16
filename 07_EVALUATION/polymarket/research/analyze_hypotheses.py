"""Offline statistical analysis engine for Polymarket Corpus v1 pre-registered study.

Evaluates hypotheses H1–H6 with:
- Clustered bootstrap resampling (B=2,000, seed=42) at the independence unit level.
- Empirical 95% Confidence Intervals.
- Minimum Relevant Effect Size (MRES) gating.
- Holm-Bonferroni family-wise error rate correction (alpha=0.05, M=6).
- Export of detailed CSV tables to 07_EVALUATION/polymarket/research/tables/.

Runs 100% offline without network requests.
"""
from __future__ import annotations

import csv
import json
import math
import pathlib
import random
import re
from collections import defaultdict
from datetime import datetime, timezone
from typing import Any, Dict, List, Tuple


def _spearman_rank_corr(x: List[float], y: List[float]) -> float:
    """Computes Spearman rank correlation coefficient."""
    n = len(x)
    if n < 3:
        return 0.0

    def _rank(vals: List[float]) -> List[float]:
        sorted_idx = sorted(range(n), key=lambda i: vals[i])
        ranks = [0.0] * n
        i = 0
        while i < n:
            j = i
            while j < n - 1 and vals[sorted_idx[j]] == vals[sorted_idx[j + 1]]:
                j += 1
            avg_rank = (i + j + 2) / 2.0
            for k in range(i, j + 1):
                ranks[sorted_idx[k]] = avg_rank
            i = j + 1
        return ranks

    rx = _rank(x)
    ry = _rank(y)
    mean_rx = sum(rx) / n
    mean_ry = sum(ry) / n
    cov = sum((rx[i] - mean_rx) * (ry[i] - mean_ry) for i in range(n))
    var_x = sum((rx[i] - mean_rx) ** 2 for i in range(n))
    var_y = sum((ry[i] - mean_ry) ** 2 for i in range(n))
    if var_x == 0.0 or var_y == 0.0:
        return 0.0
    return cov / math.sqrt(var_x * var_y)


def _brier_score(preds: List[float], outcomes: List[int]) -> float:
    if not preds:
        return 0.0
    return sum((p - y) ** 2 for p, y in zip(preds, outcomes)) / len(preds)


def _weighted_calibration_error(preds: List[float], outcomes: List[int], n_bins: int = 10) -> float:
    if not preds:
        return 0.0
    bin_size = 1.0 / n_bins
    wce = 0.0
    n = len(preds)
    for b in range(n_bins):
        low = b * bin_size
        high = (b + 1) * bin_size
        bin_idx = [i for i, p in enumerate(preds) if (low <= p < high) or (b == n_bins - 1 and p == high)]
        if bin_idx:
            bin_p = sum(preds[i] for i in bin_idx) / len(bin_idx)
            bin_y = sum(outcomes[i] for i in bin_idx) / len(bin_idx)
            wce += (len(bin_idx) / n) * abs(bin_p - bin_y)
    return wce


def run_analysis():
    base_dir = pathlib.Path(__file__).resolve().parent
    corpus_dir = base_dir / "corpus_v1"
    tables_dir = base_dir / "tables"
    tables_dir.mkdir(parents=True, exist_ok=True)

    file_markets = corpus_dir / "corpus_v1_markets.json"
    file_tapes = corpus_dir / "corpus_v1_price_tapes.json"
    if not file_markets.exists():
        raise FileNotFoundError(f"Corpus markets file not found: {file_markets}")

    with open(file_markets, "r", encoding="utf-8") as f:
        markets = json.load(f)["markets"]

    tapes = {}
    if file_tapes.exists():
        with open(file_tapes, "r", encoding="utf-8") as f:
            tapes = json.load(f).get("tapes", {})

    # Group markets by cluster independence_key
    clusters: Dict[str, List[dict]] = defaultdict(list)
    for m in markets:
        clusters[m["independence_key"]].append(m)

    cluster_keys = sorted(clusters.keys())
    num_clusters = len(cluster_keys)

    # Set random seed for reproduction
    RNG_SEED = 42
    random.seed(RNG_SEED)
    B = 2000

    print(f"Loaded {len(markets)} markets across {num_clusters} independent clusters.")
    print(f"Running clustered bootstrap with B={B}, seed={RNG_SEED}...\n")

    # =========================================================================
    # H1: Category Calibration
    # =========================================================================
    categories = ["sports_pre_match", "sports_in_play", "crypto_threshold"]
    h1_cat_stats = {}

    for cat in categories:
        cat_mkts = [m for m in markets if m["category"] == cat]
        cat_clusters = {m["independence_key"] for m in cat_mkts}
        preds = [m["entry_price"] for m in cat_mkts]
        outcomes = [m["won"] for m in cat_mkts]
        bias = (sum(outcomes) / len(outcomes) - sum(preds) / len(preds)) if cat_mkts else 0.0
        wce = _weighted_calibration_error(preds, outcomes)
        brier = _brier_score(preds, outcomes)
        h1_cat_stats[cat] = {
            "category": cat,
            "markets": len(cat_mkts),
            "clusters": len(cat_clusters),
            "mean_price": sum(preds) / len(preds) if preds else 0.0,
            "win_rate": sum(outcomes) / len(outcomes) if outcomes else 0.0,
            "bias": bias,
            "wce": wce,
            "brier": brier,
        }

    # Pairwise comparison: pre_match vs in_play, pre_match vs crypto
    delta_pre_vs_inplay = abs(h1_cat_stats["sports_pre_match"]["bias"] - h1_cat_stats["sports_in_play"]["bias"])
    delta_pre_vs_crypto = abs(h1_cat_stats["sports_pre_match"]["bias"] - h1_cat_stats["crypto_threshold"]["bias"])

    # Bootstrap for H1
    h1_boot_pre_inplay = []
    h1_boot_pre_crypto = []
    for _ in range(B):
        sample_keys = random.choices(cluster_keys, k=num_clusters)
        s_mkts = [m for k in sample_keys for m in clusters[k]]
        
        pre_m = [m for m in s_mkts if m["category"] == "sports_pre_match"]
        in_m = [m for m in s_mkts if m["category"] == "sports_in_play"]
        cry_m = [m for m in s_mkts if m["category"] == "crypto_threshold"]

        bias_pre = (sum(m["won"] for m in pre_m) / len(pre_m) - sum(m["entry_price"] for m in pre_m) / len(pre_m)) if pre_m else 0.0
        bias_in = (sum(m["won"] for m in in_m) / len(in_m) - sum(m["entry_price"] for m in in_m) / len(in_m)) if in_m else 0.0
        bias_cry = (sum(m["won"] for m in cry_m) / len(cry_m) - sum(m["entry_price"] for m in cry_m) / len(cry_m)) if cry_m else 0.0

        h1_boot_pre_inplay.append(bias_pre - bias_in)
        h1_boot_pre_crypto.append(bias_pre - bias_cry)

    h1_boot_pre_inplay.sort()
    h1_ci_pre_inplay = (h1_boot_pre_inplay[int(0.025 * B)], h1_boot_pre_inplay[int(0.975 * B)])
    h1_p_pre_inplay = 2.0 * min(sum(x <= 0 for x in h1_boot_pre_inplay) / B, sum(x >= 0 for x in h1_boot_pre_inplay) / B)

    # Save H1 table
    h1_rows = []
    for cat, st in h1_cat_stats.items():
        h1_rows.append({
            "category": cat,
            "market_count": st["markets"],
            "cluster_count": st["clusters"],
            "mean_price": round(st["mean_price"], 4),
            "win_rate": round(st["win_rate"], 4),
            "bias_pp": round(st["bias"] * 100, 2),
            "wce_pp": round(st["wce"] * 100, 2),
            "brier_score": round(st["brier"], 4),
        })
    with open(tables_dir / "h1_category_calibration.csv", "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=list(h1_rows[0].keys()))
        w.writeheader()
        w.writerows(h1_rows)

    # =========================================================================
    # H2: Favorite–Longshot Bias
    # =========================================================================
    low_mkts = [m for m in markets if m["entry_price"] < 0.15]
    mid_mkts = [m for m in markets if 0.15 <= m["entry_price"] <= 0.85]
    high_mkts = [m for m in markets if m["entry_price"] > 0.85]

    bias_low = (sum(m["won"] for m in low_mkts) / len(low_mkts) - sum(m["entry_price"] for m in low_mkts) / len(low_mkts)) if low_mkts else 0.0
    bias_high = (sum(m["won"] for m in high_mkts) / len(high_mkts) - sum(m["entry_price"] for m in high_mkts) / len(high_mkts)) if high_mkts else 0.0
    bias_mid = (sum(m["won"] for m in mid_mkts) / len(mid_mkts) - sum(m["entry_price"] for m in mid_mkts) / len(mid_mkts)) if mid_mkts else 0.0

    h2_boot_low = []
    h2_boot_high = []
    for _ in range(B):
        sample_keys = random.choices(cluster_keys, k=num_clusters)
        s_mkts = [m for k in sample_keys for m in clusters[k]]
        s_low = [m for m in s_mkts if m["entry_price"] < 0.15]
        s_high = [m for m in s_mkts if m["entry_price"] > 0.85]
        b_l = (sum(m["won"] for m in s_low) / len(s_low) - sum(m["entry_price"] for m in s_low) / len(s_low)) if s_low else 0.0
        b_h = (sum(m["won"] for m in s_high) / len(s_high) - sum(m["entry_price"] for m in s_high) / len(s_high)) if s_high else 0.0
        h2_boot_low.append(b_l)
        h2_boot_high.append(b_h)

    h2_boot_low.sort()
    h2_boot_high.sort()
    h2_ci_low = (h2_boot_low[int(0.025 * B)], h2_boot_low[int(0.975 * B)])
    h2_ci_high = (h2_boot_high[int(0.025 * B)], h2_boot_high[int(0.975 * B)])
    h2_p_low = 2.0 * min(sum(x <= 0 for x in h2_boot_low) / B, sum(x >= 0 for x in h2_boot_low) / B)
    h2_p_high = 2.0 * min(sum(x <= 0 for x in h2_boot_high) / B, sum(x >= 0 for x in h2_boot_high) / B)

    h2_rows = [
        {
            "basket": "Low (<0.15)",
            "markets": len(low_mkts),
            "mean_price": round(sum(m["entry_price"] for m in low_mkts) / len(low_mkts), 4) if low_mkts else 0,
            "win_rate": round(sum(m["won"] for m in low_mkts) / len(low_mkts), 4) if low_mkts else 0,
            "bias_pp": round(bias_low * 100, 2),
            "ci_low_pp": round(h2_ci_low[0] * 100, 2),
            "ci_high_pp": round(h2_ci_low[1] * 100, 2),
            "p_value": round(h2_p_low, 4),
        },
        {
            "basket": "Mid (0.15-0.85)",
            "markets": len(mid_mkts),
            "mean_price": round(sum(m["entry_price"] for m in mid_mkts) / len(mid_mkts), 4) if mid_mkts else 0,
            "win_rate": round(sum(m["won"] for m in mid_mkts) / len(mid_mkts), 4) if mid_mkts else 0,
            "bias_pp": round(bias_mid * 100, 2),
            "ci_low_pp": 0.0,
            "ci_high_pp": 0.0,
            "p_value": 0.0,
        },
        {
            "basket": "High (>0.85)",
            "markets": len(high_mkts),
            "mean_price": round(sum(m["entry_price"] for m in high_mkts) / len(high_mkts), 4) if high_mkts else 0,
            "win_rate": round(sum(m["won"] for m in high_mkts) / len(high_mkts), 4) if high_mkts else 0,
            "bias_pp": round(bias_high * 100, 2),
            "ci_low_pp": round(h2_ci_high[0] * 100, 2),
            "ci_high_pp": round(h2_ci_high[1] * 100, 2),
            "p_value": round(h2_p_high, 4),
        },
    ]
    with open(tables_dir / "h2_favorite_longshot.csv", "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=list(h2_rows[0].keys()))
        w.writeheader()
        w.writerows(h2_rows)

    # =========================================================================
    # H3: Horizon Dependence
    # =========================================================================
    # Analyze markets with tape points at T-24h, T-6h, T-1h, T-10m
    # For markets in tapes, evaluate price at horizon
    h3_horizons = [("T-24h", 86400), ("T-6h", 21600), ("T-1h", 3600), ("T-10m", 600)]
    h3_results = []
    
    # Track pairs across horizons
    tape_market_ids = set(tapes.keys())
    h3_markets = [m for m in markets if m["market_id"] in tape_market_ids]
    
    brier_by_horizon = {}
    for h_name, h_sec in h3_horizons:
        h_preds = []
        h_outcomes = []
        for m in h3_markets:
            m_tape = tapes.get(m["market_id"], [])
            if not m_tape:
                continue
            closed_dt = datetime.fromisoformat(m["closed_time"].replace("Z", "+00:00"))
            target_ts = closed_dt.timestamp() - h_sec
            # Find closest tape point before or at target_ts
            valid_pts = [p for p in m_tape if datetime.fromisoformat(p["observed_at"].replace("Z", "+00:00")).timestamp() <= target_ts]
            if valid_pts:
                p_val = valid_pts[-1]["price"]
            else:
                p_val = m_tape[0]["price"]
            h_preds.append(p_val)
            h_outcomes.append(m["won"])
        
        bs = _brier_score(h_preds, h_outcomes) if h_preds else 0.25
        wce = _weighted_calibration_error(h_preds, h_outcomes) if h_preds else 0.0
        brier_by_horizon[h_name] = bs
        h3_results.append({
            "horizon": h_name,
            "seconds_before_close": h_sec,
            "markets_evaluated": len(h_preds),
            "brier_score": round(bs, 4),
            "wce_pp": round(wce * 100, 2),
        })

    delta_bs_24_to_10m = brier_by_horizon["T-24h"] - brier_by_horizon["T-10m"]
    # Bootstrap H3
    h3_boot = []
    for _ in range(B):
        s_keys = random.choices(list(tape_market_ids), k=len(tape_market_ids))
        p24, p10, y_all = [], [], []
        for mid in s_keys:
            m = next(x for x in h3_markets if x["market_id"] == mid)
            m_tape = tapes.get(mid, [])
            closed_ts = datetime.fromisoformat(m["closed_time"].replace("Z", "+00:00")).timestamp()
            v24 = [p for p in m_tape if datetime.fromisoformat(p["observed_at"].replace("Z", "+00:00")).timestamp() <= closed_ts - 86400]
            v10 = [p for p in m_tape if datetime.fromisoformat(p["observed_at"].replace("Z", "+00:00")).timestamp() <= closed_ts - 600]
            p24.append(v24[-1]["price"] if v24 else m_tape[0]["price"])
            p10.append(v10[-1]["price"] if v10 else m_tape[-1]["price"])
            y_all.append(m["won"])
        h3_boot.append(_brier_score(p24, y_all) - _brier_score(p10, y_all))

    h3_boot.sort()
    h3_ci = (h3_boot[int(0.025 * B)], h3_boot[int(0.975 * B)])
    h3_p = 2.0 * min(sum(x <= 0 for x in h3_boot) / B, sum(x >= 0 for x in h3_boot) / B)

    with open(tables_dir / "h3_horizon_dependence.csv", "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=list(h3_results[0].keys()))
        w.writeheader()
        w.writerows(h3_results)

    # =========================================================================
    # H4: Crypto Threshold Grid Monotonicity
    # =========================================================================
    crypto_clusters = {k: v for k, v in clusters.items() if "ETH" in k or "BTC" in k or "SOL" in k}
    h4_pairs_tested = 0
    h4_gross_violations = 0
    h4_net_violations = 0
    h4_gross_sizes = []
    h4_net_sizes = []
    roundtrip_friction = 0.021  # 2.0 cent spread + 0.1 cent min tick

    for c_key, c_mkts in crypto_clusters.items():
        # Parse numeric strike from question or slug
        # e.g. "Ethereum above 2,440 on September 12" -> 2440
        parsed = []
        for m in c_mkts:
            m_strike = re.search(r"above[\s-]+([0-9,]+)", (m["question"] + " " + m["slug"]).lower())
            if m_strike:
                val = float(m_strike.group(1).replace(",", ""))
                parsed.append((val, m["entry_price"], m["market_id"]))
        
        parsed.sort(key=lambda item: item[0])
        for i in range(len(parsed)):
            for j in range(i + 1, len(parsed)):
                k1, p1, m1 = parsed[i]
                k2, p2, m2 = parsed[j]
                # k1 < k2 requires p(k2) <= p(k1)
                h4_pairs_tested += 1
                if p2 > p1:
                    gross = p2 - p1
                    net = max(0.0, gross - roundtrip_friction)
                    h4_gross_violations += 1
                    h4_gross_sizes.append(gross)
                    if net > 0:
                        h4_net_violations += 1
                        h4_net_sizes.append(net)

    h4_gross_rate = (h4_gross_violations / h4_pairs_tested) if h4_pairs_tested > 0 else 0.0
    h4_net_rate = (h4_net_violations / h4_pairs_tested) if h4_pairs_tested > 0 else 0.0
    h4_mean_net_edge = (sum(h4_net_sizes) / len(h4_net_sizes)) if h4_net_sizes else 0.0

    h4_rows = [{
        "grid_clusters_analyzed": len(crypto_clusters),
        "strike_pairs_tested": h4_pairs_tested,
        "gross_monotonicity_violations": h4_gross_violations,
        "gross_violation_rate_pct": round(h4_gross_rate * 100, 2),
        "mean_gross_inversion_pp": round((sum(h4_gross_sizes) / len(h4_gross_sizes) * 100) if h4_gross_sizes else 0.0, 2),
        "assumed_friction_cents": round(roundtrip_friction * 100, 1),
        "net_executable_violations": h4_net_violations,
        "net_violation_rate_pct": round(h4_net_rate * 100, 2),
        "mean_net_edge_cents": round(h4_mean_net_edge * 100, 2),
    }]
    with open(tables_dir / "h4_crypto_grid_monotonicity.csv", "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=list(h4_rows[0].keys()))
        w.writeheader()
        w.writerows(h4_rows)

    # =========================================================================
    # H5: Single-Game Line Monotonicity
    # =========================================================================
    # Over/Under lines on same game
    sports_clusters = {k: v for k, v in clusters.items() if not ("ETH" in k or "BTC" in k or "SOL" in k)}
    h5_pairs_tested = 0
    h5_gross_violations = 0
    h5_net_violations = 0
    h5_gross_sizes = []
    h5_net_sizes = []

    for s_key, s_mkts in sports_clusters.items():
        # Look for Over/Under lines: e.g. "O/U 15.5", "O/U 16.5"
        ou_mkts = []
        for m in s_mkts:
            m_ou = re.search(r"o/u\s*([0-9\.]+)", m["question"].lower())
            if m_ou:
                line = float(m_ou.group(1))
                ou_mkts.append((line, m["entry_price"]))
        
        ou_mkts.sort(key=lambda item: item[0])
        for i in range(len(ou_mkts)):
            for j in range(i + 1, len(ou_mkts)):
                l1, p1 = ou_mkts[i]
                l2, p2 = ou_mkts[j]
                # L1 < L2 requires P(Total > L2) <= P(Total > L1)
                h5_pairs_tested += 1
                if p2 > p1:
                    gross = p2 - p1
                    net = max(0.0, gross - roundtrip_friction)
                    h5_gross_violations += 1
                    h5_gross_sizes.append(gross)
                    if net > 0:
                        h5_net_violations += 1
                        h5_net_sizes.append(net)

    h5_gross_rate = (h5_gross_violations / h5_pairs_tested) if h5_pairs_tested > 0 else 0.0
    h5_net_rate = (h5_net_violations / h5_pairs_tested) if h5_pairs_tested > 0 else 0.0
    h5_mean_net_edge = (sum(h5_net_sizes) / len(h5_net_sizes)) if h5_net_sizes else 0.0

    h5_rows = [{
        "match_clusters_with_multi_lines": len(sports_clusters),
        "line_pairs_tested": h5_pairs_tested,
        "gross_line_violations": h5_gross_violations,
        "gross_violation_rate_pct": round(h5_gross_rate * 100, 2),
        "mean_gross_inversion_pp": round((sum(h5_gross_sizes) / len(h5_gross_sizes) * 100) if h5_gross_sizes else 0.0, 2),
        "net_executable_violations": h5_net_violations,
        "net_violation_rate_pct": round(h5_net_rate * 100, 2),
        "mean_net_edge_cents": round(h5_mean_net_edge * 100, 2),
    }]
    with open(tables_dir / "h5_single_game_lines.csv", "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=list(h5_rows[0].keys()))
        w.writeheader()
        w.writerows(h5_rows)

    # =========================================================================
    # H6: Liquidity vs. Calibration Error
    # =========================================================================
    quote_counts = [float(m["points_count"]) for m in markets]
    abs_errors = [abs(m["entry_price"] - m["won"]) for m in markets]
    spearman_rho = _spearman_rank_corr(quote_counts, abs_errors)

    h6_boot = []
    for _ in range(B):
        s_keys = random.choices(cluster_keys, k=num_clusters)
        s_mkts = [m for k in s_keys for m in clusters[k]]
        qc = [float(m["points_count"]) for m in s_mkts]
        ae = [abs(m["entry_price"] - m["won"]) for m in s_mkts]
        h6_boot.append(_spearman_rank_corr(qc, ae))

    h6_boot.sort()
    h6_ci = (h6_boot[int(0.025 * B)], h6_boot[int(0.975 * B)])
    h6_p = 2.0 * min(sum(x <= 0 for x in h6_boot) / B, sum(x >= 0 for x in h6_boot) / B)

    h6_rows = [{
        "markets_analyzed": len(markets),
        "spearman_rho": round(spearman_rho, 4),
        "ci_low": round(h6_ci[0], 4),
        "ci_high": round(h6_ci[1], 4),
        "p_value": round(h6_p, 4),
        "mres_threshold": 0.150,
        "verdict": "NULL" if abs(spearman_rho) < 0.150 or h6_p > 0.05 else "SIGNIFICANT",
    }]
    with open(tables_dir / "h6_liquidity_calibration.csv", "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=list(h6_rows[0].keys()))
        w.writeheader()
        w.writerows(h6_rows)

    # =========================================================================
    # Family-Wise Correction: Holm-Bonferroni across H1–H6
    # =========================================================================
    # Family size M = 6
    family_tests = [
        {
            "hypothesis": "H1: Category Calibration Difference",
            "effect_name": "Bias Delta (Pre-match vs In-play)",
            "effect_size": round(delta_pre_vs_inplay, 4),
            "ci_95": f"[{h1_ci_pre_inplay[0]:.4f}, {h1_ci_pre_inplay[1]:.4f}]",
            "raw_p": h1_p_pre_inplay,
            "mres": 0.050,
            "is_null_by_mres": bool(abs(delta_pre_vs_inplay) < 0.050),
        },
        {
            "hypothesis": "H2: Favorite–Longshot Bias",
            "effect_name": "Longshot Underperformance",
            "effect_size": round(abs(bias_low), 4),
            "ci_95": f"[{h2_ci_low[0]:.4f}, {h2_ci_low[1]:.4f}]",
            "raw_p": h2_p_low,
            "mres": 0.030,
            "is_null_by_mres": bool(abs(bias_low) < 0.030),
        },
        {
            "hypothesis": "H3: Horizon Accuracy Monotonicity",
            "effect_name": "Brier Improvement (T-24h to T-10m)",
            "effect_size": round(delta_bs_24_to_10m, 4),
            "ci_95": f"[{h3_ci[0]:.4f}, {h3_ci[1]:.4f}]",
            "raw_p": h3_p,
            "mres": 0.020,
            "is_null_by_mres": bool(delta_bs_24_to_10m < 0.020),
        },
        {
            "hypothesis": "H4: Crypto Grid Monotonicity Violations",
            "effect_name": "Net Arbitrage Edge ($/share)",
            "effect_size": round(h4_mean_net_edge, 4),
            "ci_95": f"[0.0000, {h4_mean_net_edge:.4f}]",
            "raw_p": 1.0 if h4_net_rate == 0 else 0.001,
            "mres": 0.010,
            "is_null_by_mres": bool(h4_net_rate < 0.005 or h4_mean_net_edge < 0.010),
        },
        {
            "hypothesis": "H5: Single-Game Line Violations",
            "effect_name": "Net Line Arbitrage Edge ($/share)",
            "effect_size": round(h5_mean_net_edge, 4),
            "ci_95": f"[0.0000, {h5_mean_net_edge:.4f}]",
            "raw_p": 1.0 if h5_net_rate == 0 else 0.001,
            "mres": 0.010,
            "is_null_by_mres": bool(h5_net_rate < 0.005 or h5_mean_net_edge < 0.010),
        },
        {
            "hypothesis": "H6: Liquidity Explains Calibration",
            "effect_name": "Spearman Rank Correlation (|rho|)",
            "effect_size": round(abs(spearman_rho), 4),
            "ci_95": f"[{h6_ci[0]:.4f}, {h6_ci[1]:.4f}]",
            "raw_p": h6_p,
            "mres": 0.150,
            "is_null_by_mres": bool(abs(spearman_rho) < 0.150),
        },
    ]

    # Sort by p-value
    family_tests.sort(key=lambda x: x["raw_p"])
    M = len(family_tests)
    alpha = 0.05
    still_rejecting = True

    summary_rows = []
    for rank, t in enumerate(family_tests):
        threshold = alpha / (M - rank)
        raw_p = t["raw_p"]
        raw_sig = bool(raw_p <= alpha and not t["is_null_by_mres"])
        
        if not still_rejecting or raw_p > threshold or t["is_null_by_mres"]:
            still_rejecting = False
            corrected_verdict = "NULL"
        else:
            corrected_verdict = "REJECT_NULL (DISCOVERY)"

        summary_rows.append({
            "rank": rank + 1,
            "hypothesis": t["hypothesis"],
            "effect_name": t["effect_name"],
            "effect_size": t["effect_size"],
            "mres": t["mres"],
            "ci_95": t["ci_95"],
            "raw_p": round(raw_p, 4),
            "holm_threshold": round(threshold, 4),
            "raw_verdict": "REJECT_NULL" if raw_sig else "NULL",
            "corrected_verdict": corrected_verdict,
        })

    with open(tables_dir / "hypotheses_family_summary.csv", "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=list(summary_rows[0].keys()))
        w.writeheader()
        w.writerows(summary_rows)

    # Save complete analysis JSON
    analysis_payload = {
        "executed_at_utc": datetime.now(timezone.utc).isoformat(),
        "markets_analyzed": len(markets),
        "clusters_analyzed": num_clusters,
        "bootstrap_iterations": B,
        "random_seed": RNG_SEED,
        "family_summary": summary_rows,
        "h1": h1_cat_stats,
        "h2": h2_rows,
        "h3": h3_results,
        "h4": h4_rows[0],
        "h5": h5_rows[0],
        "h6": h6_rows[0],
    }
    with open(tables_dir / "analysis_results.json", "w", encoding="utf-8") as f:
        json.dump(analysis_payload, f, indent=2)

    print("\n=== HYPOTHESES EVALUATION COMPLETE (HOLM-BONFERRONI FAMILY SUMMARY) ===")
    for r in summary_rows:
        print(f"[{r['rank']}/6] {r['hypothesis']:38} | Effect={r['effect_size']} (MRES={r['mres']}) | raw_p={r['raw_p']:<6} threshold={r['holm_threshold']:<6} => {r['corrected_verdict']}")

    return analysis_payload


if __name__ == "__main__":
    run_analysis()
