"""
fetch_costs.py — Calculează profilul empiric al costurilor de tranzacționare (B3):
1. Distribuția spread-ului pe fiecare oră a zilei (00:00 - 23:00) din datele orare H1 (10,000 bare).
2. Efectul de lărgire a spread-ului la rollover-ul de la miezul nopții (ora 00:00).
3. Costul de finanțare peste noapte (swap long/short) și impactul zilei de swap triplu.

Generează:
- 07_EVALUATION/metatrader/research/tables/table_3_costs_hourly.csv
- 07_EVALUATION/metatrader/research/tables/table_3_costs_summary.csv
"""

import csv
import json
import os
import sys
from datetime import datetime
import numpy as np

BASE_DIR = os.path.join("07_EVALUATION", "metatrader", "research")
CORPUS_H1 = os.path.join(BASE_DIR, "corpus", "H1")
TABLES_DIR = os.path.join(BASE_DIR, "tables")
CENSUS_CSV = os.path.join(TABLES_DIR, "table_1_census.csv")
QUALITY_CSV = os.path.join(TABLES_DIR, "table_2_quality.csv")
HOURLY_CSV = os.path.join(TABLES_DIR, "table_3_costs_hourly.csv")
SUMMARY_CSV = os.path.join(TABLES_DIR, "table_3_costs_summary.csv")


def main():
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--out-dir", default=TABLES_DIR, help="Output directory for tables")
    args = parser.parse_args()
    out_dir = args.out_dir
    os.makedirs(out_dir, exist_ok=True)
    hourly_csv = os.path.join(out_dir, "table_3_costs_hourly.csv")
    summary_csv = os.path.join(out_dir, "table_3_costs_summary.csv")

    if not os.path.exists(CENSUS_CSV) or not os.path.exists(QUALITY_CSV):
        print("Missing prerequisites: table_1_census.csv or table_2_quality.csv", file=sys.stderr)
        sys.exit(1)

    meta_dict = {}
    with open(CENSUS_CSV, "r", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        for r in reader:
            meta_dict[r["symbol"]] = r

    eligible_symbols = set()
    with open(QUALITY_CSV, "r", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        for r in reader:
            if r["eligible"].lower() == "true":
                eligible_symbols.add(r["symbol"])

    print(f"Analyzing hourly spread and costs for {len(eligible_symbols)} eligible symbols...", flush=True)

    hourly_records = []
    summary_records = []

    for sym in sorted(eligible_symbols):
        meta = meta_dict.get(sym, {})
        h1_file = os.path.join(CORPUS_H1, f"{sym}_H1.csv")
        if not os.path.exists(h1_file):
            continue

        hour_spreads = {h: [] for h in range(24)}
        all_spreads = []

        with open(h1_file, "r", encoding="utf-8") as f:
            reader = csv.DictReader(f)
            for row in reader:
                dt = datetime.strptime(row["time"], "%Y-%m-%d %H:%M:%S")
                sp = float(row["spread"])
                if sp > 0:  # valid recorded floating spread
                    hour_spreads[dt.hour].append(sp)
                    all_spreads.append(sp)

        if not all_spreads:
            continue

        all_arr = np.array(all_spreads)
        med_overall = float(np.median(all_arr))
        p5_overall = float(np.percentile(all_arr, 5))
        p95_overall = float(np.percentile(all_arr, 95))

        # Hourly breakdown
        for h in range(24):
            arr_h = np.array(hour_spreads[h]) if len(hour_spreads[h]) > 0 else np.array([med_overall])
            med_h = float(np.median(arr_h))
            mean_h = float(np.mean(arr_h))
            p90_h = float(np.percentile(arr_h, 90))

            hourly_records.append({
                "symbol": sym,
                "category": meta.get("category", "UNKNOWN"),
                "hour_utc": h,
                "median_spread_points": round(med_h, 1),
                "mean_spread_points": round(mean_h, 1),
                "p90_spread_points": round(p90_h, 1),
                "samples_count": len(arr_h)
            })

        # Rollover hour (00:00) vs Daily daytime median (e.g. London/NY hours 08:00-16:00)
        sp_00 = np.median(hour_spreads[0]) if len(hour_spreads[0]) > 0 else med_overall
        day_spreads = [sp for h in range(8, 17) for sp in hour_spreads[h]]
        sp_day = np.median(day_spreads) if len(day_spreads) > 0 else med_overall
        rollover_multiplier = round(sp_00 / sp_day, 2) if sp_day > 0 else 1.0

        summary_records.append({
            "symbol": sym,
            "category": meta.get("category", "UNKNOWN"),
            "point": meta.get("point"),
            "contract_size": meta.get("contract_size"),
            "median_spread_points": round(med_overall, 1),
            "p5_spread_points": round(p5_overall, 1),
            "p95_spread_points": round(p95_overall, 1),
            "rollover_00h_spread_points": round(float(sp_00), 1),
            "daytime_spread_points": round(float(sp_day), 1),
            "rollover_expansion_multiplier": rollover_multiplier,
            "swap_long": meta.get("swap_long"),
            "swap_short": meta.get("swap_short"),
            "swap_rollover3days": meta.get("swap_rollover3days")
        })

    # Save Hourly CSV
    fieldnames_hourly = [
        "symbol", "category", "hour_utc", "median_spread_points", "mean_spread_points",
        "p90_spread_points", "samples_count"
    ]
    with open(hourly_csv, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames_hourly)
        writer.writeheader()
        for r in hourly_records:
            writer.writerow(r)

    # Save Summary CSV
    fieldnames_summary = [
        "symbol", "category", "point", "contract_size", "median_spread_points",
        "p5_spread_points", "p95_spread_points", "rollover_00h_spread_points",
        "daytime_spread_points", "rollover_expansion_multiplier", "swap_long",
        "swap_short", "swap_rollover3days"
    ]
    with open(summary_csv, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames_summary)
        writer.writeheader()
        for r in summary_records:
            writer.writerow(r)

    print(f"Generated {HOURLY_CSV} ({len(hourly_records)} rows)")
    print(f"Generated {SUMMARY_CSV} ({len(summary_records)} symbols)")


if __name__ == "__main__":
    main()
