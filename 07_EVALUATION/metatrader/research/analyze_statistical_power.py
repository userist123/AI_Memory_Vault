"""
analyze_statistical_power.py — Analiza de Putere Statistică a Studiului V1
Calculează Minimum Detectable Sharpe Ratio (MDSR) la o putere de 80% (alpha = 0.05).
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
stationary_bootstrap_indices = boot_mod.stationary_bootstrap_indices

TABLES_DIR = os.path.join(BASE_DIR, "tables")
QUALITY_CSV = os.path.join(TABLES_DIR, "table_2_quality.csv")
SUMMARY_CSV = os.path.join(TABLES_DIR, "table_3_costs_summary.csv")


def fast_hansen_spa(d: np.ndarray, b_reps: int = 500, q: float = 10.0, seed: int = 42) -> float:
    t_len, m_models = d.shape
    d_bar = np.mean(d, axis=0)

    p_geom = 1.0 / q
    rng = np.random.RandomState(seed)

    all_indices = np.empty((b_reps, t_len), dtype=np.int64)
    for b in range(b_reps):
        cur = rng.randint(0, t_len)
        all_indices[b, 0] = cur
        for t in range(1, t_len):
            if rng.rand() < p_geom:
                cur = rng.randint(0, t_len)
            else:
                cur = (cur + 1) % t_len
            all_indices[b, t] = cur

    boot_means = np.empty((b_reps, m_models), dtype=np.float64)
    for b in range(b_reps):
        boot_means[b, :] = np.mean(d[all_indices[b]], axis=0)

    se_hat = np.std(boot_means, axis=0)
    se_hat = np.where(se_hat < 1e-12, 1e-12, se_hat)

    t_k = d_bar / se_hat
    t_spa_obs = float(np.max(np.maximum(t_k, 0.0)))

    c_thresh = -np.sqrt(2.0 * np.log(max(np.log(max(t_len, 3)), 1.01))) * se_hat
    g_hansen = np.where(d_bar >= c_thresh, d_bar, 0.0)

    z_b_hansen = (boot_means - g_hansen) / se_hat
    t_spa_boot = np.max(np.maximum(z_b_hansen, 0.0), axis=1)

    p_val_spa = float(np.mean(t_spa_boot >= t_spa_obs))
    return p_val_spa


def main():
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--out-dir", default=TABLES_DIR, help="Output directory for tables")
    args = parser.parse_args()
    out_dir = args.out_dir

    eligible_symbols = []
    with open(QUALITY_CSV, "r", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        for r in reader:
            if r["eligible"].lower() == "true":
                eligible_symbols.append(r["symbol"])

    meta_dict = {}
    with open(SUMMARY_CSV, "r", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        for r in reader:
            meta_dict[r["symbol"]] = r

    strategies = [
        "S1_MOM_20", "S1_MOM_60", "S1_MOM_120", "S1_MOM_252",
        "S2_MR_W20", "S2_MR_W50",
        "S3_BRK_20", "S3_BRK_50",
        "S4_CARRY",
    ]

    val_returns_matrix = {}
    for sym in eligible_symbols:
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
    m_active = len(keys)

    empirical_matrix = np.empty((min_val_len, m_active), dtype=np.float64)
    for j, k in enumerate(keys):
        empirical_matrix[:, j] = val_returns_matrix[k][:min_val_len]

    stds = np.std(empirical_matrix, axis=0)

    # Grid extins pentru a gasi exact punctul de 80% putere
    sharpe_grid = [0.25, 0.50, 0.75, 1.00, 1.25, 1.50, 2.00, 2.50, 3.00, 3.50, 4.00, 4.50, 5.00]
    n_sims = 150
    q_bootstrap = 10.0

    master_rng = np.random.RandomState(42)
    results = []
    planted_strategy_idx = 0

    print(f"Matrice validare: {min_val_len} zile x {m_active} strategii")
    print(f"Rulare power analysis pe {len(sharpe_grid)} paliere Sharpe cu N={n_sims} simulari...")

    for sr_target in sharpe_grid:
        mu_daily = (sr_target * stds[planted_strategy_idx]) / np.sqrt(252)

        rejections = 0
        p_values = []

        for sim_i in range(n_sims):
            sim_seed = master_rng.randint(0, 1000000)
            rng_sim = np.random.RandomState(sim_seed)

            idx_sim = stationary_bootstrap_indices(min_val_len, q_bootstrap, rng_sim)
            sim_data = empirical_matrix[idx_sim, :].copy()

            sim_data = sim_data - np.mean(sim_data, axis=0)
            sim_data[:, planted_strategy_idx] += mu_daily

            p_val = fast_hansen_spa(sim_data, b_reps=500, q=q_bootstrap, seed=sim_seed)
            p_values.append(p_val)
            if p_val < 0.05:
                rejections += 1

        power = rejections / n_sims
        z = 1.96
        p_hat = power
        denom = 1 + z**2 / n_sims
        center = (p_hat + z**2 / (2 * n_sims)) / denom
        delta = z * np.sqrt((p_hat * (1 - p_hat) + z**2 / (4 * n_sims)) / n_sims) / denom
        ci_low = max(0.0, center - delta)
        ci_high = min(1.0, center + delta)

        mean_p = float(np.mean(p_values))
        median_p = float(np.median(p_values))

        results.append({
            "injected_true_sharpe": sr_target,
            "simulations_count": n_sims,
            "rejections_count": rejections,
            "statistical_power_1_minus_beta": round(power, 4),
            "power_ci_lower_95": round(ci_low, 4),
            "power_ci_upper_95": round(ci_high, 4),
            "mean_spa_p_value": round(mean_p, 4),
            "median_spa_p_value": round(median_p, 4),
        })

        print(f"SR={sr_target:4.2f} | Putere: {power:6.2%} (CI 95%: [{ci_low:6.2%}, {ci_high:6.2%}]) | Mean p: {mean_p:.4f} | Med p: {median_p:.4f}")

    # Interpolare MDSR @ 80%
    srs = [r["injected_true_sharpe"] for r in results]
    powers = [r["statistical_power_1_minus_beta"] for r in results]

    mdsr_80 = None
    for i in range(len(powers) - 1):
        if powers[i] <= 0.80 <= powers[i+1]:
            slope = (srs[i+1] - srs[i]) / (powers[i+1] - powers[i])
            mdsr_80 = srs[i] + slope * (0.80 - powers[i])
            break

    if mdsr_80 is None:
        mdsr_80 = srs[-1] if powers[-1] < 0.80 else srs[0]

    print(f"\n=======================================================")
    print(f"Minimum Detectable Sharpe Ratio (MDSR @ 80% power): {mdsr_80:.4f}")
    print(f"=======================================================\n")

    out_csv = os.path.join(out_dir, "table_8_power_analysis.csv")
    fieldnames = [
        "injected_true_sharpe", "simulations_count", "rejections_count",
        "statistical_power_1_minus_beta", "power_ci_lower_95", "power_ci_upper_95",
        "mean_spa_p_value", "median_spa_p_value"
    ]
    with open(out_csv, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        for r in results:
            writer.writerow(r)

    print(f"Salvat tabelul in {out_csv}")


if __name__ == "__main__":
    main()
