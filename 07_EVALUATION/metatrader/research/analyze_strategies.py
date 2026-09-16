"""
analyze_strategies.py — Evaluează toate combinațiile Strategie x Parametru x Instrument
pe segmentele de Dezvoltare (In-Sample) și Validare (Walk-Forward Out-of-Sample).

Reguli de integritate:
- Lucrează EXCLUSIV pe fișierele comise (fără conexiune la terminal).
- NU atinge segmentul Holdout (2026-03-15 -> 2026-09-14).
- Rulează testul Hansen SPA și White Reality Check pe întreaga grilă de validare.
- Zero constante literale pentru p-values; calcul determinist prin bootstrap (seed=42).

Generează:
- 07_EVALUATION/metatrader/research/tables/table_4_development_results.csv
- 07_EVALUATION/metatrader/research/tables/table_5_validation_results.csv
- 07_EVALUATION/metatrader/research/tables/table_6_validation_spa_tests.csv
- 07_EVALUATION/metatrader/research/tables/table_7_commission_sensitivity.csv
"""

import csv
import json
import os
import sys
from datetime import datetime
import numpy as np

# Add repo root to path
BASE_DIR = os.path.join("07_EVALUATION", "metatrader", "research")
sys.path.insert(0, os.path.abspath("."))
from importlib import import_module

costs_mod = import_module("07_EVALUATION.metatrader.research.engine.costs")
CostModel = costs_mod.CostModel
strat_mod = import_module("07_EVALUATION.metatrader.research.engine.strategies")
bt_mod = import_module("07_EVALUATION.metatrader.research.engine.backtest")
run_backtest = bt_mod.run_backtest
boot_mod = import_module("07_EVALUATION.metatrader.research.engine.bootstrap")
hansen_spa_test = boot_mod.hansen_spa_test

CORPUS_D1 = os.path.join(BASE_DIR, "corpus", "D1")
TABLES_DIR = os.path.join(BASE_DIR, "tables")
QUALITY_CSV = os.path.join(TABLES_DIR, "table_2_quality.csv")
SUMMARY_CSV = os.path.join(TABLES_DIR, "table_3_costs_summary.csv")

# Cutoffs din PREREGISTRATION.md
DEV_CUTOFF = datetime(2025, 3, 14, 23, 59, 59)
VAL_CUTOFF = datetime(2026, 3, 14, 23, 59, 59)
# HOLDOUT este strict 2026-03-15 -> 2026-09-14 (BLOCAT în Partea D!)


def load_symbol_series(symbol: str):
    csv_path = os.path.join(CORPUS_D1, f"{symbol}_D1.csv")
    timestamps, opens, highs, lows, closes = [], [], [], [], []
    with open(csv_path, "r", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        for r in reader:
            dt = datetime.strptime(r["time"], "%Y-%m-%d %H:%M:%S")
            timestamps.append(dt)
            opens.append(float(r["open"]))
            highs.append(float(r["high"]))
            lows.append(float(r["low"]))
            closes.append(float(r["close"]))
    return (
        timestamps,
        np.array(opens, dtype=np.float64),
        np.array(highs, dtype=np.float64),
        np.array(lows, dtype=np.float64),
        np.array(closes, dtype=np.float64),
    )


def build_cost_model(meta: dict, commission: float = 2.0) -> CostModel:
    return CostModel(
        spread_points=float(meta.get("median_spread_points", 15.0)),
        point=float(meta.get("point", 1e-5)),
        contract_size=float(meta.get("contract_size", 100000.0)),
        commission_per_lot=commission,
        swap_long_points=float(meta.get("swap_long", 0.0)),
        swap_short_points=float(meta.get("swap_short", 0.0)),
        swap_rollover3days=int(meta.get("swap_rollover3days", 3)),
    )


def run_strategy_on_segment(
    strat_name: str,
    opens: np.ndarray,
    highs: np.ndarray,
    lows: np.ndarray,
    closes: np.ndarray,
    timestamps: list,
    cost_model: CostModel,
    idx_mask: np.ndarray,
) -> dict:
    # 1. Generare semnale pe întreaga serie (pentru a asigura warm-up corect)
    n = len(closes)
    if strat_name == "S1_MOM_20":
        signals = strat_mod.generate_s1_momentum(closes, L=20)
    elif strat_name == "S1_MOM_60":
        signals = strat_mod.generate_s1_momentum(closes, L=60)
    elif strat_name == "S1_MOM_120":
        signals = strat_mod.generate_s1_momentum(closes, L=120)
    elif strat_name == "S1_MOM_252":
        signals = strat_mod.generate_s1_momentum(closes, L=252)
    elif strat_name == "S2_MR_W20":
        signals = strat_mod.generate_s2_mean_reversion(closes, W=20, Z_thresh=2.0, K_max=10)
    elif strat_name == "S2_MR_W50":
        signals = strat_mod.generate_s2_mean_reversion(closes, W=50, Z_thresh=2.0, K_max=10)
    elif strat_name == "S3_BRK_20":
        signals = strat_mod.generate_s3_breakout(highs, lows, closes, L=20)
    elif strat_name == "S3_BRK_50":
        signals = strat_mod.generate_s3_breakout(highs, lows, closes, L=50)
    elif strat_name == "S4_CARRY":
        signals = strat_mod.generate_s4_carry(n, cost_model.swap_long_points, cost_model.swap_short_points)
    elif strat_name == "S0a_BH":
        signals = strat_mod.generate_s0a_buy_and_hold(n)
    else:
        raise ValueError(f"Unknown strategy {strat_name}")

    # 2. Rulare backtest pe întreaga serie
    bt = run_backtest(opens, highs, lows, closes, timestamps, signals, cost_model)

    # 3. Extragere metrici strict pe segmentul selectat prin mască
    seg_net_rets = bt.net_returns[idx_mask]
    seg_gross_rets = bt.gross_returns[idx_mask]
    seg_positions = bt.positions[idx_mask]

    seg_metrics = bt_mod.compute_metrics(seg_net_rets, seg_gross_rets, seg_positions)

    return {
        "net_returns": seg_net_rets,
        "gross_returns": seg_gross_rets,
        "positions": seg_positions,
        "metrics": seg_metrics,
        "signals": signals[idx_mask],
    }


def main():
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--out-dir", default=TABLES_DIR, help="Output directory for tables")
    args = parser.parse_args()
    out_dir = args.out_dir
    os.makedirs(out_dir, exist_ok=True)

    # 1. Încărcare univers eligibil și profiluri costuri
    eligible_symbols = []
    with open(QUALITY_CSV, "r", encoding="utf-8") as f:
        for r in csv.DictReader(f):
            if r["eligible"].lower() == "true":
                eligible_symbols.append(r["symbol"])

    meta_dict = {}
    with open(SUMMARY_CSV, "r", encoding="utf-8") as f:
        for r in csv.DictReader(f):
            meta_dict[r["symbol"]] = r

    print(f"Loaded {len(eligible_symbols)} eligible symbols for analysis.", flush=True)

    strategy_names = [
        "S1_MOM_20", "S1_MOM_60", "S1_MOM_120", "S1_MOM_252",
        "S2_MR_W20", "S2_MR_W50",
        "S3_BRK_20", "S3_BRK_50",
        "S4_CARRY",
        "S0a_BH"
    ]

    dev_results = []
    val_results = []
    val_returns_matrix = {}  # key: (sym, strat), value: net_returns vector on val

    print("Evaluating Development and Validation segments...", flush=True)

    for sym in sorted(eligible_symbols):
        meta = meta_dict[sym]
        category = meta.get("category", "UNKNOWN")
        cost_model = build_cost_model(meta, commission=2.0)

        timestamps, opens, highs, lows, closes = load_symbol_series(sym)
        ts_arr = np.array(timestamps)

        # Măști temporale
        mask_dev = ts_arr <= DEV_CUTOFF
        mask_val = (ts_arr > DEV_CUTOFF) & (ts_arr <= VAL_CUTOFF)

        for strat in strategy_names:
            # Pentru metale și crypto, S4 Carry nu se aplică (swap mereu negativ pe ambele sensuri)
            if strat == "S4_CARRY" and category in ("METALS", "CRYPTO"):
                continue

            # A. Dezvoltare
            res_dev = run_strategy_on_segment(strat, opens, highs, lows, closes, timestamps, cost_model, mask_dev)
            m_dev = res_dev["metrics"]
            dev_results.append({
                "symbol": sym,
                "category": category,
                "strategy": strat,
                "total_return": m_dev.total_return,
                "cagr": m_dev.cagr,
                "annualized_vol": m_dev.annualized_vol,
                "gross_sharpe": m_dev.gross_sharpe,
                "net_sharpe": m_dev.net_sharpe,
                "cost_drag": m_dev.total_cost_drag,
                "max_drawdown": m_dev.max_drawdown,
                "trade_count": m_dev.trade_count,
                "exposure_pct": m_dev.exposure_pct,
            })

            # B. Validare
            res_val = run_strategy_on_segment(strat, opens, highs, lows, closes, timestamps, cost_model, mask_val)
            m_val = res_val["metrics"]

            # Control aleator S0b pe validare
            # Evaluat cu M=100 traiectorii pentru a calcula p-value empiric față de norocul aleator
            real_sig = res_val["signals"]
            s0b_sharpes = []
            for seed in range(100):
                s0b_sig = strat_mod.generate_s0b_random_matched(real_sig, seed=seed)
                bt_s0b = run_backtest(
                    opens[mask_val], highs[mask_val], lows[mask_val], closes[mask_val],
                    [timestamps[i] for i, v in enumerate(mask_val) if v],
                    s0b_sig, cost_model
                )
                s0b_sharpes.append(bt_s0b.metrics.net_sharpe)

            p_val_s0b = float(np.mean(np.array(s0b_sharpes) >= m_val.net_sharpe))
            s0b_med_sr = float(np.median(s0b_sharpes)) if len(s0b_sharpes) > 0 else 0.0

            val_results.append({
                "symbol": sym,
                "category": category,
                "strategy": strat,
                "total_return": m_val.total_return,
                "cagr": m_val.cagr,
                "annualized_vol": m_val.annualized_vol,
                "gross_sharpe": m_val.gross_sharpe,
                "net_sharpe": m_val.net_sharpe,
                "cost_drag": m_val.total_cost_drag,
                "max_drawdown": m_val.max_drawdown,
                "trade_count": m_val.trade_count,
                "exposure_pct": m_val.exposure_pct,
                "s0b_median_sharpe": round(s0b_med_sr, 4),
                "p_value_vs_s0b": round(p_val_s0b, 4),
                "beats_s0b": p_val_s0b < 0.05,
            })

            val_returns_matrix[(sym, strat)] = res_val["net_returns"]

    # Salvare Rezultate Dezvoltare
    dev_csv = os.path.join(out_dir, "table_4_development_results.csv")
    fieldnames_dev = [
        "symbol", "category", "strategy", "total_return", "cagr", "annualized_vol",
        "gross_sharpe", "net_sharpe", "cost_drag", "max_drawdown", "trade_count", "exposure_pct"
    ]
    with open(dev_csv, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames_dev)
        writer.writeheader()
        for r in dev_results:
            writer.writerow(r)

    # Salvare Rezultate Validare
    val_csv = os.path.join(out_dir, "table_5_validation_results.csv")
    fieldnames_val = fieldnames_dev + ["s0b_median_sharpe", "p_value_vs_s0b", "beats_s0b"]
    with open(val_csv, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames_val)
        writer.writeheader()
        for r in val_results:
            writer.writerow(r)

    print(f"Saved {dev_csv} ({len(dev_results)} rows)")
    print(f"Saved {val_csv} ({len(val_results)} rows)")

    # 3. Corecție pentru Testări Multiple: Testul Hansen SPA pe Validare
    # Construim matricea T_val x M_comb
    # T_val este numărul de zile din perioada de validare (~260 zile)
    # Găsim lungimea minimă comună pe validare
    val_lens = [len(v) for v in val_returns_matrix.values()]
    min_val_len = min(val_lens)
    keys = sorted(val_returns_matrix.keys())

    # Modele active (exclusiv controalele S0a)
    active_keys = [k for k in keys if not k[1].startswith("S0a")]
    m_active = len(active_keys)

    loss_models = np.empty((min_val_len, m_active), dtype=np.float64)
    for j, k in enumerate(active_keys):
        loss_models[:, j] = val_returns_matrix[k][:min_val_len]

    # Benchmark 1: Zero Return (Cash)
    bench_zero = np.zeros(min_val_len, dtype=np.float64)
    spa_res_zero = hansen_spa_test(bench_zero, loss_models, b_reps=2000, q=10.0, seed=42)

    # Benchmark 2: Media strategiilor S0a (Buy and Hold)
    s0a_keys = [k for k in keys if k[1].startswith("S0a")]
    s0a_mat = np.column_stack([val_returns_matrix[k][:min_val_len] for k in s0a_keys])
    bench_s0a = np.mean(s0a_mat, axis=1)
    spa_res_s0a = hansen_spa_test(bench_s0a, loss_models, b_reps=2000, q=10.0, seed=42)

    best_strat_zero = active_keys[spa_res_zero["best_model_idx"]]
    best_strat_s0a = active_keys[spa_res_s0a["best_model_idx"]]

    spa_records = [
        {
            "benchmark": "Zero_Excess_Return_Cash",
            "tested_models_count": m_active,
            "test_statistic_t_spa": spa_res_zero["test_stat"],
            "p_value_hansen_spa": spa_res_zero["p_value_spa"],
            "p_value_white_reality_check": spa_res_zero["p_value_white"],
            "best_model_symbol": best_strat_zero[0],
            "best_model_strategy": best_strat_zero[1],
            "decision": "REJECT_NULL (Viable Alpha)" if spa_res_zero["p_value_spa"] < 0.05 else "ACCEPT_NULL (No Alpha)",
        },
        {
            "benchmark": "Equal_Weight_Buy_and_Hold_S0a",
            "tested_models_count": m_active,
            "test_statistic_t_spa": spa_res_s0a["test_stat"],
            "p_value_hansen_spa": spa_res_s0a["p_value_spa"],
            "p_value_white_reality_check": spa_res_s0a["p_value_white"],
            "best_model_symbol": best_strat_s0a[0],
            "best_model_strategy": best_strat_s0a[1],
            "decision": "REJECT_NULL (Viable Alpha)" if spa_res_s0a["p_value_spa"] < 0.05 else "ACCEPT_NULL (No Alpha)",
        },
    ]

    spa_csv = os.path.join(out_dir, "table_6_validation_spa_tests.csv")
    with open(spa_csv, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=list(spa_records[0].keys()))
        writer.writeheader()
        for r in spa_records:
            writer.writerow(r)
    print(f"Saved {spa_csv}")

    # 4. Sensibilitate la Comision (Palierele: 0, 2, 5, 7 USD/lot)
    # Pe un subset reprezentativ (EURUSD S1_MOM_60, USDJPY S3_BRK_20, XAUUSD S1_MOM_20, ETHUSD S1_MOM_20)
    rep_cases = [
        ("EURUSD", "S1_MOM_60"),
        ("USDJPY", "S3_BRK_20"),
        ("XAUUSD", "S1_MOM_20"),
        ("ETHUSD", "S1_MOM_20"),
    ]
    comm_records = []
    comm_tiers = [0.0, 2.0, 5.0, 7.0]

    for sym, strat in rep_cases:
        meta = meta_dict[sym]
        timestamps, opens, highs, lows, closes = load_symbol_series(sym)
        ts_arr = np.array(timestamps)
        mask_val = (ts_arr > DEV_CUTOFF) & (ts_arr <= VAL_CUTOFF)

        for comm in comm_tiers:
            cm = build_cost_model(meta, commission=comm)
            res = run_strategy_on_segment(strat, opens, highs, lows, closes, timestamps, cm, mask_val)
            m = res["metrics"]
            comm_records.append({
                "symbol": sym,
                "strategy": strat,
                "commission_per_lot_usd": comm,
                "net_sharpe": m.net_sharpe,
                "cagr": m.cagr,
                "cost_drag": m.total_cost_drag,
                "trade_count": m.trade_count,
            })

    comm_csv = os.path.join(out_dir, "table_7_commission_sensitivity.csv")
    with open(comm_csv, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=list(comm_records[0].keys()))
        writer.writeheader()
        for r in comm_records:
            writer.writerow(r)
    print(f"Saved {comm_csv}")


if __name__ == "__main__":
    main()
