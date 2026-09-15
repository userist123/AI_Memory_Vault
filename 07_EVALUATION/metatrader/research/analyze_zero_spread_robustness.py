"""
analyze_zero_spread_robustness.py — Verificarea robusteții rezultatului nul
la excluderea instrumentelor cu număr ridicat de bare spread == 0.
"""

import csv
import os
import sys
from datetime import datetime
import numpy as np

BASE_DIR = os.path.join("07_EVALUATION", "metatrader", "research")
sys.path.insert(0, os.path.abspath("."))
from importlib import import_module

strat_analysis = import_module("07_EVALUATION.metatrader.research.analyze_strategies")
load_symbol_series = strat_analysis.load_symbol_series
build_cost_model = strat_analysis.build_cost_model
run_strategy_on_segment = strat_analysis.run_strategy_on_segment
DEV_CUTOFF = strat_analysis.DEV_CUTOFF
VAL_CUTOFF = strat_analysis.VAL_CUTOFF

boot_mod = import_module("07_EVALUATION.metatrader.research.engine.bootstrap")
hansen_spa_test = boot_mod.hansen_spa_test

TABLES_DIR = os.path.join(BASE_DIR, "tables")
QUALITY_CSV = os.path.join(TABLES_DIR, "table_2_quality.csv")
SUMMARY_CSV = os.path.join(TABLES_DIR, "table_3_costs_summary.csv")


def main():
    eligible_symbols = []
    with open(QUALITY_CSV, "r", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        for r in reader:
            if r["eligible"].lower() == "true":
                eligible_symbols.append({
                    "symbol": r["symbol"],
                    "category": r["category"],
                    "zero_spread_bars": int(r["zero_spread_bars"]),
                    "d1_bars": int(r["d1_bars"]),
                })

    # Sortare descrescator dupa zero_spread_bars
    eligible_symbols.sort(key=lambda x: x["zero_spread_bars"], reverse=True)
    n_total = len(eligible_symbols)
    q_cutoff = n_total // 4

    top_q_excluded = eligible_symbols[:q_cutoff]
    clean_retained = eligible_symbols[q_cutoff:]

    print(f"Total instrumente eligibile V1: {n_total}")
    print(f"Top 25% excluse (cele mai multe bare spread == 0): {[x['symbol'] for x in top_q_excluded]}")
    for x in top_q_excluded:
        print(f"  - {x['symbol']} ({x['category']}): {x['zero_spread_bars']} zero-spread bars din {x['d1_bars']}")

    print(f"\nRetinute curate ({len(clean_retained)}): {[x['symbol'] for x in clean_retained]}")

    meta_dict = {}
    with open(SUMMARY_CSV, "r", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        for r in reader:
            meta_dict[r["symbol"]] = r

    clean_symbols = [x["symbol"] for x in clean_retained]
    strategies = [
        "S1_MOM_20", "S1_MOM_60", "S1_MOM_120", "S1_MOM_252",
        "S2_MR_W20", "S2_MR_W50",
        "S3_BRK_20", "S3_BRK_50",
        "S4_CARRY",
        "S0a_BH",
    ]

    val_returns_matrix = {}
    for sym in clean_symbols:
        meta = meta_dict[sym]
        cm = build_cost_model(meta, commission=2.0)
        timestamps, opens, highs, lows, closes = load_symbol_series(sym)
        ts_arr = np.array(timestamps)
        mask_val = (ts_arr > DEV_CUTOFF) & (ts_arr <= VAL_CUTOFF)

        for strat in strategies:
            res = run_strategy_on_segment(strat, opens, highs, lows, closes, timestamps, cm, mask_val)
            val_returns_matrix[(sym, strat)] = res["net_returns"]

    min_val_len = min(len(v) for v in val_returns_matrix.values())
    keys = sorted(val_returns_matrix.keys())

    active_keys = [k for k in keys if not k[1].startswith("S0a")]
    m_active = len(active_keys)

    loss_models = np.empty((min_val_len, m_active), dtype=np.float64)
    for j, k in enumerate(active_keys):
        loss_models[:, j] = val_returns_matrix[k][:min_val_len]

    # Benchmark 1: Cash
    bench_zero = np.zeros(min_val_len, dtype=np.float64)
    spa_zero = hansen_spa_test(bench_zero, loss_models, b_reps=2000, q=10.0, seed=42)

    # Benchmark 2: S0a Buy & Hold pe universul curat
    s0a_keys = [k for k in keys if k[1].startswith("S0a")]
    s0a_mat = np.column_stack([val_returns_matrix[k][:min_val_len] for k in s0a_keys])
    bench_s0a = np.mean(s0a_mat, axis=1)
    spa_s0a = hansen_spa_test(bench_s0a, loss_models, b_reps=2000, q=10.0, seed=42)

    best_zero = active_keys[spa_zero["best_model_idx"]]
    best_s0a = active_keys[spa_s0a["best_model_idx"]]

    print("\n--- REZULTATE SPA PE UNIVERSUL CURAT (21 INSTRUMENTE) ---")
    print(f"Modele active testate: {m_active}")
    print(f"Vs. Cash: T_SPA = {spa_zero['test_stat']:.4f}, p_SPA = {spa_zero['p_value_spa']:.4f}, p_White = {spa_zero['p_value_white']:.4f}, Cel mai bun: {best_zero}")
    print(f"Vs. Buy & Hold: T_SPA = {spa_s0a['test_stat']:.4f}, p_SPA = {spa_s0a['p_value_spa']:.4f}, p_White = {spa_s0a['p_value_white']:.4f}, Cel mai bun: {best_s0a}")

    records = [
        {
            "universe_subset": "Clean_75pct_Excluding_Top_Quartile_Zero_Spread",
            "excluded_symbols": ";".join(x["symbol"] for x in top_q_excluded),
            "retained_symbols_count": len(clean_symbols),
            "benchmark": "Zero_Excess_Return_Cash",
            "tested_models_count": m_active,
            "test_statistic_t_spa": spa_zero["test_stat"],
            "p_value_hansen_spa": spa_zero["p_value_spa"],
            "p_value_white_reality_check": spa_zero["p_value_white"],
            "best_model_symbol": best_zero[0],
            "best_model_strategy": best_zero[1],
            "decision": "REJECT_NULL (Viable Alpha)" if spa_zero["p_value_spa"] < 0.05 else "ACCEPT_NULL (No Alpha)",
        },
        {
            "universe_subset": "Clean_75pct_Excluding_Top_Quartile_Zero_Spread",
            "excluded_symbols": ";".join(x["symbol"] for x in top_q_excluded),
            "retained_symbols_count": len(clean_symbols),
            "benchmark": "Equal_Weight_Buy_and_Hold_S0a",
            "tested_models_count": m_active,
            "test_statistic_t_spa": spa_s0a["test_stat"],
            "p_value_hansen_spa": spa_s0a["p_value_spa"],
            "p_value_white_reality_check": spa_s0a["p_value_white"],
            "best_model_symbol": best_s0a[0],
            "best_model_strategy": best_s0a[1],
            "decision": "REJECT_NULL (Viable Alpha)" if spa_s0a["p_value_spa"] < 0.05 else "ACCEPT_NULL (No Alpha)",
        }
    ]

    out_csv = os.path.join(TABLES_DIR, "table_6b_zero_spread_robustness.csv")
    with open(out_csv, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=list(records[0].keys()))
        writer.writeheader()
        for r in records:
            writer.writerow(r)

    print(f"Salvat cu succes: {out_csv}")


if __name__ == "__main__":
    main()
