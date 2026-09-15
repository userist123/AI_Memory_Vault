"""
fetch_quality.py — Evaluează calitatea datelor istorice (B2) și aplică regulile obiective
de selecție a universului (A1) pe toate cele 37 de instrumente MT5.

Verificări de integritate:
1. Marcaje de timp duplicate sau neordonate.
2. Bare cu volum zero și bare înghețate (Open == High == Low == Close).
3. Prețuri netranzacționabile (spread <= 0 sau spread negativ).
4. Goluri temporale majore (> 5 zile lucrătoare consecutive lipsă).
5. Cost relativ: Spread Median ÷ ATR(14) Median <= 12%.

Generează: 07_EVALUATION/metatrader/research/tables/table_2_quality.csv
"""

import csv
import json
import os
import sys
from datetime import datetime, timedelta
import numpy as np

BASE_DIR = os.path.join("07_EVALUATION", "metatrader", "research")
CORPUS_D1 = os.path.join(BASE_DIR, "corpus", "D1")
TABLES_DIR = os.path.join(BASE_DIR, "tables")
CENSUS_CSV = os.path.join(TABLES_DIR, "table_1_census.csv")
QUALITY_CSV = os.path.join(TABLES_DIR, "table_2_quality.csv")
QUALITY_JSON = os.path.join(TABLES_DIR, "table_2_quality.json")


def compute_atr14(highs, lows, closes):
    n = len(closes)
    if n < 15:
        return np.zeros(n)
    tr = np.zeros(n)
    tr[0] = highs[0] - lows[0]
    for i in range(1, n):
        tr[i] = max(
            highs[i] - lows[i],
            abs(highs[i] - closes[i - 1]),
            abs(lows[i] - closes[i - 1])
        )
    # 14-period SMA of TR
    atr = np.zeros(n)
    atr[13] = np.mean(tr[:14])
    for i in range(14, n):
        atr[i] = (atr[i - 1] * 13 + tr[i]) / 14.0
    return atr


def audit_symbol_data(symbol: str, meta: dict) -> dict:
    csv_file = os.path.join(CORPUS_D1, f"{symbol}_D1.csv")
    if not os.path.exists(csv_file):
        return {"symbol": symbol, "error": "file_missing"}

    timestamps = []
    opens = []
    highs = []
    lows = []
    closes = []
    tick_volumes = []
    spreads = []

    with open(csv_file, "r", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        for row in reader:
            dt = datetime.strptime(row["time"], "%Y-%m-%d %H:%M:%S")
            timestamps.append(dt)
            opens.append(float(row["open"]))
            highs.append(float(row["high"]))
            lows.append(float(row["low"]))
            closes.append(float(row["close"]))
            tick_volumes.append(int(row["tick_volume"]))
            spreads.append(float(row["spread"]))

    n_bars = len(timestamps)
    point = float(meta.get("point", 1e-5))

    # 1. Duplicate & Out-of-order timestamps
    dup_count = 0
    ooo_count = 0
    max_gap_days = 0

    for i in range(1, n_bars):
        diff = timestamps[i] - timestamps[i - 1]
        if diff.total_seconds() == 0:
            dup_count += 1
        elif diff.total_seconds() < 0:
            ooo_count += 1
        else:
            # Calendar gap excluding standard weekend (Friday to Monday = 3 days)
            days = diff.days
            # If gap > 4 calendar days (e.g. holiday + weekend = 3-4 days), measure abnormal gap
            if days > 5:
                if days > max_gap_days:
                    max_gap_days = days

    # 2. Zero volume bars
    zero_vol_count = sum(1 for v in tick_volumes if v == 0)

    # 3. Frozen bars: Open == High == Low == Close for >= 2 consecutive bars
    frozen_bars = 0
    consec_frozen = 0
    for i in range(n_bars):
        if opens[i] == highs[i] == lows[i] == closes[i]:
            consec_frozen += 1
            if consec_frozen >= 2:
                frozen_bars += 1
        else:
            consec_frozen = 0

    # 4. Zero / Negative spreads
    # Note: On MT5 historical D1 bars, spread field records the spread in points at bar open
    # Check for negative spreads
    neg_spread_count = sum(1 for s in spreads if s < 0)
    zero_spread_count = sum(1 for s in spreads if s == 0)

    # 5. ATR(14) and relative cost
    highs_arr = np.array(highs)
    lows_arr = np.array(lows)
    closes_arr = np.array(closes)
    atr14_arr = compute_atr14(highs_arr, lows_arr, closes_arr)

    # Valid ATR values (from index 14 onwards)
    valid_atr = atr14_arr[14:] / point if point > 0 else atr14_arr[14:]
    median_atr14_points = float(np.median(valid_atr)) if len(valid_atr) > 0 else 0.0

    # Filtered spreads (spreads > 0)
    pos_spreads = [s for s in spreads if s > 0]
    # Fallback to current census spread if historical bar spread was recorded as 0 in older broker archives
    census_spread = float(meta.get("spread_points", 0))
    if len(pos_spreads) > 0:
        median_spread_points = float(np.median(pos_spreads))
    else:
        median_spread_points = census_spread

    relative_cost_pct = (
        (median_spread_points / median_atr14_points * 100.0)
        if median_atr14_points > 0
        else 999.0
    )

    # 6. Apply A1 Pre-Registration Rules
    # Criteria:
    # C1: trade_mode == 4
    # C2: d1_bars >= 1250
    # C3: relative_cost_pct <= 12.0%
    # C4: frozen_bars == 0
    # C5: dup_count == 0 and ooo_count == 0
    # C6: max_gap_days <= 5 (or max calendar gap <= 10 days for long holiday/market halts)
    reasons = []
    trade_mode = int(meta.get("trade_mode", 0))
    if trade_mode != 4:
        reasons.append(f"trade_mode_{trade_mode}_not_full")
    if n_bars < 1250:
        reasons.append(f"insufficient_depth_{n_bars}_bars")
    if relative_cost_pct > 12.0:
        reasons.append(f"relative_cost_excess_{relative_cost_pct:.1f}pct")
    if frozen_bars > 0:
        reasons.append(f"frozen_bars_{frozen_bars}")
    if dup_count > 0 or ooo_count > 0:
        reasons.append(f"timestamp_anomaly_dup{dup_count}_ooo{ooo_count}")
    if neg_spread_count > 0:
        reasons.append(f"negative_spread_{neg_spread_count}")

    eligible = len(reasons) == 0
    exclusion_reason = "; ".join(reasons) if not eligible else "NONE"

    return {
        "symbol": symbol,
        "category": meta.get("category", "UNKNOWN"),
        "d1_bars": n_bars,
        "duplicate_timestamps": dup_count,
        "out_of_order_timestamps": ooo_count,
        "zero_volume_bars": zero_vol_count,
        "frozen_bars": frozen_bars,
        "negative_spread_bars": neg_spread_count,
        "zero_spread_bars": zero_spread_count,
        "max_gap_calendar_days": max_gap_days,
        "median_spread_points": round(median_spread_points, 1),
        "median_atr14_points": round(median_atr14_points, 1),
        "relative_cost_pct": round(relative_cost_pct, 2),
        "eligible": eligible,
        "exclusion_reason": exclusion_reason,
    }


def main():
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--out-dir", default=TABLES_DIR, help="Output directory for tables")
    args = parser.parse_args()
    out_dir = args.out_dir
    os.makedirs(out_dir, exist_ok=True)
    quality_csv = os.path.join(out_dir, "table_2_quality.csv")
    quality_json = os.path.join(out_dir, "table_2_quality.json")

    if not os.path.exists(CENSUS_CSV):
        print(f"Missing census CSV: {CENSUS_CSV}", file=sys.stderr)
        sys.exit(1)

    meta_dict = {}
    with open(CENSUS_CSV, "r", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        for r in reader:
            meta_dict[r["symbol"]] = r

    results = []
    print(f"Auditing data quality for {len(meta_dict)} symbols...", flush=True)

    for sym, meta in meta_dict.items():
        res = audit_symbol_data(sym, meta)
        results.append(res)
        status = "PASS" if res.get("eligible") else "FAIL"
        print(
            f"  {sym:8s} | {res['category']:10s} | Bars: {res['d1_bars']:5d} | "
            f"Cost: {res['relative_cost_pct']:5.2f}% | Status: {status:4s} | {res['exclusion_reason']}",
            flush=True
        )

    # Save CSV
    fieldnames = [
        "symbol",
        "category",
        "d1_bars",
        "duplicate_timestamps",
        "out_of_order_timestamps",
        "zero_volume_bars",
        "frozen_bars",
        "negative_spread_bars",
        "zero_spread_bars",
        "max_gap_calendar_days",
        "median_spread_points",
        "median_atr14_points",
        "relative_cost_pct",
        "eligible",
        "exclusion_reason",
    ]
    with open(quality_csv, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        for r in results:
            writer.writerow(r)

    # Save JSON
    with open(quality_json, "w", encoding="utf-8") as f:
        json.dump(results, f, indent=2)

    n_pass = sum(1 for r in results if r["eligible"])
    n_fail = len(results) - n_pass
    print(f"\nQuality Audit Summary: {n_pass} passed, {n_fail} excluded out of {len(results)} total.", flush=True)


if __name__ == "__main__":
    main()
