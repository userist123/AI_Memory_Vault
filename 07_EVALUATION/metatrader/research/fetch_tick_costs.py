"""
fetch_tick_costs.py — Auditul Costurilor Reale din Tick-uri vs. Bare D1

Analizează spread-ul efectiv de execuție din tick-uri reale (copy_ticks_range)
pe un eșantion reprezentativ din cele 28 de instrumente eligibile,
cu accent pe momentul de execuție al barei D1 (fereastra de rollover 23:55 - 00:15 Server Time / EET-EEST, nu UTC).

Generează:
- 07_EVALUATION/metatrader/research/tables/table_9_tick_cost_comparison.csv
"""

import csv
from datetime import datetime
import os
import sys
import numpy as np

try:
    import MetaTrader5 as mt5
except ImportError:
    mt5 = None

BASE_DIR = os.path.join("07_EVALUATION", "metatrader", "research")
TABLES_DIR = os.path.join(BASE_DIR, "tables")
SUMMARY_CSV = os.path.join(TABLES_DIR, "table_3_costs_summary.csv")


def main():
    if mt5 is None:
        print("MetaTrader5 package not installed.")
        sys.exit(1)

    if not mt5.initialize():
        print(f"MT5 initialization failed: {mt5.last_error()}")
        sys.exit(1)

    # 1. Încarcă costurile din table_3
    v1_costs = {}
    with open(SUMMARY_CSV, "r", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        for r in reader:
            v1_costs[r["symbol"]] = {
                "category": r["category"],
                "point": float(r["point"]),
                "median_spread_points_v1": float(r["median_spread_points"]),
                "rollover_v1": float(r["rollover_00h_spread_points"]),
                "daytime_v1": float(r["daytime_spread_points"]),
                "multiplier_v1": float(r["rollover_expansion_multiplier"]),
                "contract_size": float(r["contract_size"]),
            }

    # Instrumente reprezentative din toate clasele
    sample_symbols = [
        "EURUSD", "USDJPY", "GBPUSD", "AUDUSD", "USDCAD", "USDCHF",
        "EURGBP", "GBPJPY",
        "XAUUSD",
        "ETHUSD",
    ]

    # Interval de 5 zile de tranzacționare recente (septembrie 2026)
    dt_to = datetime(2026, 9, 12, 0, 0, 0)
    dt_from = datetime(2026, 9, 7, 0, 0, 0)

    records = []

    print(f"Extragere tick-uri din MT5 pentru {len(sample_symbols)} simboluri intre {dt_from} si {dt_to}...")

    for sym in sample_symbols:
        meta = v1_costs.get(sym)
        if not meta:
            continue

        point = meta["point"]
        v1_spread_pts = meta["median_spread_points_v1"]

        # Asigură-te că simbolul este selectat în MarketWatch
        mt5.symbol_select(sym, True)

        ticks = mt5.copy_ticks_range(sym, dt_from, dt_to, mt5.COPY_TICKS_ALL)
        if ticks is None or len(ticks) == 0:
            print(f"Avertisment: Niciun tick pentru {sym} intre {dt_from} si {dt_to}, incercare pe ultimele zile active...")
            dt_now = datetime.now()
            dt_past = dt_now.replace(day=max(1, dt_now.day - 5))
            ticks = mt5.copy_ticks_range(sym, dt_past, dt_now, mt5.COPY_TICKS_ALL)

        if ticks is None or len(ticks) == 0:
            print(f"Eroare: Imposibil de extras tick-uri pentru {sym}: {mt5.last_error()}")
            continue

        n_ticks = len(ticks)
        ask = ticks["ask"]
        bid = ticks["bid"]
        spread_pts = (ask - bid) / point

        valid_mask = (ask > 0) & (bid > 0) & (spread_pts > 0)
        valid_spreads = spread_pts[valid_mask]
        valid_times = ticks["time"][valid_mask]

        if len(valid_spreads) == 0:
            continue

        overall_median_pts = float(np.median(valid_spreads))
        overall_mean_pts = float(np.mean(valid_spreads))
        p95_pts = float(np.percentile(valid_spreads, 95))

        # Fereastra de rollover la trecerea dintre zile: 23:55:00 - 00:15:00 Server Time (EET/EEST, broker server clock, nu UTC)
        sec_in_day = valid_times % 86400
        rollover_mask = (sec_in_day >= 86100) | (sec_in_day <= 900)
        rollover_spreads = valid_spreads[rollover_mask]

        if len(rollover_spreads) > 0:
            rollover_median_pts = float(np.median(rollover_spreads))
            rollover_mean_pts = float(np.mean(rollover_spreads))
            rollover_multiplier = rollover_median_pts / max(overall_median_pts, 1e-6)
        else:
            rollover_median_pts = overall_median_pts
            rollover_mean_pts = overall_mean_pts
            rollover_multiplier = 1.0

        discrepancy_vs_v1 = (overall_median_pts - v1_spread_pts) / max(v1_spread_pts, 1e-6) * 100.0

        # Impact estimat asupra strategiilor cu turnover ridicat (ex: 50 trades/an):
        # Cost suplimentar pe trade (in points) daca executia se face in fereastra de rollover:
        cost_penalty_per_trade_pts = max(0.0, rollover_median_pts - v1_spread_pts)

        records.append({
            "symbol": sym,
            "category": meta["category"],
            "ticks_analyzed": n_ticks,
            "v1_bar_median_spread_points": round(v1_spread_pts, 2),
            "tick_overall_median_spread_points": round(overall_median_pts, 2),
            "tick_overall_mean_spread_points": round(overall_mean_pts, 2),
            "tick_p95_spread_points": round(p95_pts, 2),
            "rollover_00h_median_spread_points": round(rollover_median_pts, 2),
            "rollover_expansion_multiplier_ticks": round(rollover_multiplier, 2),
            "v1_rollover_expansion_multiplier": round(meta["multiplier_v1"], 2),
            "rollover_penalty_per_trade_points": round(cost_penalty_per_trade_pts, 2),
            "discrepancy_tick_vs_v1_pct": round(discrepancy_vs_v1, 1),
        })

        print(f"[{sym}] Ticks: {n_ticks:,} | V1 Bar: {v1_spread_pts:.1f} pts | Tick Med: {overall_median_pts:.1f} pts | Rollover: {rollover_median_pts:.1f} pts (x{rollover_multiplier:.2f})")

    mt5.shutdown()

    out_csv = os.path.join(TABLES_DIR, "table_9_tick_cost_comparison.csv")
    if records:
        fieldnames = list(records[0].keys())
        with open(out_csv, "w", newline="", encoding="utf-8") as f:
            writer = csv.DictWriter(f, fieldnames=fieldnames)
            writer.writeheader()
            for r in records:
                writer.writerow(r)
        print(f"\nSalvat cu succes in {out_csv} ({len(records)} simboluri analizate)")
    else:
        print("Nu au fost generate inregistrari!")


if __name__ == "__main__":
    main()
