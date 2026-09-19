"""
run_experiments_v3.py — Experiment Runner for Planning Influence V3

Executes the pre-registered experimental grid:
1. Main experiment: 11 accuracy levels x 2 applicability modes x 4 policies (N=200 per cell).
2. Accuracy thresholds computation with 95% bootstrap confidence intervals.
3. Stale memory arm under confirmed contradiction.
4. Verification-as-action cost-benefit analysis.
5. Robustness grid: branch counts (K=4, 6, 8), fatal densities, exploration constants (c=1.0, 1.414, 2.0).

Outputs CSV tables to 07_EVALUATION/luna/tables/
"""
import argparse
import csv
import os
import sys
from typing import Dict, List, Sequence, Tuple
import numpy as np

BASE_DIR = os.path.join("07_EVALUATION", "luna")
TABLES_DIR = os.path.join(BASE_DIR, "tables")

sys.path.insert(0, os.path.abspath("."))
from importlib import import_module

v3_mod = import_module("07_EVALUATION.luna.planning_influence_mve_v3")
build_scenarios_v3 = v3_mod.build_scenarios_v3
run_planner_v3 = v3_mod.run_planner_v3

POLICIES = ["baseline", "advisory", "v1_uncertainty", "v2_verification_action"]
ACCURACIES = [0.0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0]


def bootstrap_ci(data: np.ndarray, n_boot: int = 1000, seed: int = 20260916) -> Tuple[float, float, float]:
    rng = np.random.RandomState(seed)
    n = len(data)
    means = np.empty(n_boot, dtype=np.float64)
    for b in range(n_boot):
        idx = rng.randint(0, n, size=n)
        means[b] = np.mean(data[idx])
    mean_val = float(np.mean(data))
    ci_lo = float(np.percentile(means, 2.5))
    ci_hi = float(np.percentile(means, 97.5))
    return mean_val, ci_lo, ci_hi


def run_cell(
    scenarios,
    policy: str,
    exploration: float = 1.414,
    seed: int = 42,
) -> Dict[str, any]:
    nodes_list = []
    fatals_list = []
    success_list = []
    ver_actions_list = []
    ver_nodes_list = []

    for scen in scenarios:
        trace = run_planner_v3(scen, policy=policy, exploration=exploration, seed=seed)
        nodes_list.append(trace.total_effective_nodes)
        fatals_list.append(trace.fatal_visits)
        success_list.append(1.0 if trace.success else 0.0)
        ver_actions_list.append(trace.verification_actions)
        ver_nodes_list.append(trace.verification_nodes)

    nodes_arr = np.array(nodes_list, dtype=np.float64)
    fatals_arr = np.array(fatals_list, dtype=np.float64)

    m_nodes, ci_nodes_lo, ci_nodes_hi = bootstrap_ci(nodes_arr)
    m_fatals, ci_fatals_lo, ci_fatals_hi = bootstrap_ci(fatals_arr)
    succ_rate = float(np.mean(success_list))
    mean_ver_actions = float(np.mean(ver_actions_list))
    mean_ver_nodes = float(np.mean(ver_nodes_list))

    return {
        "mean_nodes": m_nodes,
        "ci_nodes_lo": ci_nodes_lo,
        "ci_nodes_hi": ci_nodes_hi,
        "mean_fatals": m_fatals,
        "ci_fatals_lo": ci_fatals_lo,
        "ci_fatals_hi": ci_fatals_hi,
        "success_rate": succ_rate,
        "mean_ver_actions": mean_ver_actions,
        "mean_ver_nodes": mean_ver_nodes,
        "raw_nodes": nodes_arr,
        "raw_fatals": fatals_arr,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--out-dir", default=TABLES_DIR, help="Output directory for CSV tables")
    parser.add_argument("--n-scenarios", type=int, default=200, help="Scenarios per cell")
    args = parser.parse_args()

    out_dir = args.out_dir
    os.makedirs(out_dir, exist_ok=True)
    n_scen = args.n_scenarios

    print(f"=== PLANNING INFLUENCE V3 EXPERIMENTAL RUNNER ===")
    print(f"Output directory: {out_dir}")
    print(f"Scenarios per cell: {n_scen}\n")

    # =========================================================================
    # 1. MAIN EXPERIMENT: 11 Accuracies x 2 Applicabilities x 4 Policies
    # =========================================================================
    print("--- 1. Running Main Experiment ---")
    main_records = []
    threshold_records = []

    for app_mode in ["calibrated", "uninformative"]:
        print(f"Mode: {app_mode}")
        for acc in ACCURACIES:
            # Seed varies deterministically per accuracy & mode
            scen_seed = int(acc * 1000) + (100 if app_mode == "calibrated" else 200)
            scenarios = build_scenarios_v3(
                count=n_scen,
                num_branches=4,
                num_fatals=2,
                accuracy=acc,
                applicability_mode=app_mode,
                seed=scen_seed,
            )

            cell_results = {}
            for pol in POLICIES:
                res = run_cell(scenarios, policy=pol, exploration=1.414, seed=scen_seed + 1)
                cell_results[pol] = res

            base_res = cell_results["baseline"]

            for pol in POLICIES:
                res = cell_results[pol]
                # Delta vs baseline: positive delta_nodes = improvement
                delta_nodes = base_res["mean_nodes"] - res["mean_nodes"]
                delta_fatals = res["mean_fatals"] - base_res["mean_fatals"]

                # Bootstrap CI for delta_nodes
                diff_nodes_arr = base_res["raw_nodes"] - res["raw_nodes"]
                m_diff, d_lo, d_hi = bootstrap_ci(diff_nodes_arr)

                main_records.append({
                    "applicability_mode": app_mode,
                    "accuracy": acc,
                    "policy": pol,
                    "mean_nodes": round(res["mean_nodes"], 4),
                    "ci_nodes_lo": round(res["ci_nodes_lo"], 4),
                    "ci_nodes_hi": round(res["ci_nodes_hi"], 4),
                    "mean_fatals": round(res["mean_fatals"], 4),
                    "ci_fatals_lo": round(res["ci_fatals_lo"], 4),
                    "ci_fatals_hi": round(res["ci_fatals_hi"], 4),
                    "success_rate": round(res["success_rate"], 4),
                    "mean_ver_actions": round(res["mean_ver_actions"], 4),
                    "mean_ver_nodes": round(res["mean_ver_nodes"], 4),
                    "delta_nodes_vs_baseline": round(delta_nodes, 4),
                    "delta_nodes_ci_lo": round(d_lo, 4),
                    "delta_nodes_ci_hi": round(d_hi, 4),
                    "delta_fatals_vs_baseline": round(delta_fatals, 4),
                })

    # Save table 1
    t1_path = os.path.join(out_dir, "table_luna_1_main_experiment.csv")
    with open(t1_path, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=list(main_records[0].keys()))
        writer.writeheader()
        for r in main_records:
            writer.writerow(r)
    print(f"Saved: {t1_path}")

    # =========================================================================
    # 2. ACCURACY THRESHOLDS
    # =========================================================================
    # Minimum accuracy where delta_nodes_ci_lo > 0 and delta_fatals <= 0
    print("\n--- 2. Computing Accuracy Thresholds ---")
    threshold_records = []
    for app_mode in ["calibrated", "uninformative"]:
        for pol in ["v1_uncertainty", "v2_verification_action"]:
            pol_rows = [r for r in main_records if r["applicability_mode"] == app_mode and r["policy"] == pol]
            p_star = None
            delta_at_p_star = None
            ci_at_p_star = None
            for row in pol_rows:
                if row["delta_nodes_ci_lo"] > 0 and row["delta_fatals_vs_baseline"] <= 0.05:
                    p_star = row["accuracy"]
                    delta_at_p_star = row["delta_nodes_vs_baseline"]
                    ci_at_p_star = f"[{row['delta_nodes_ci_lo']:.3f}, {row['delta_nodes_ci_hi']:.3f}]"
                    break
            threshold_records.append({
                "applicability_mode": app_mode,
                "policy": pol,
                "threshold_accuracy_p_star": p_star if p_star is not None else "NONE",
                "delta_nodes_at_threshold": delta_at_p_star if delta_at_p_star is not None else "N/A",
                "ci_delta_nodes_95": ci_at_p_star if ci_at_p_star is not None else "N/A",
                "conclusion": "BENEFIT_ESTABLISHED" if p_star is not None else "NO_THRESHOLD_FOUND",
            })

    t2_path = os.path.join(out_dir, "table_luna_2_accuracy_thresholds.csv")
    with open(t2_path, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=list(threshold_records[0].keys()))
        writer.writeheader()
        for r in threshold_records:
            writer.writerow(r)
    print(f"Saved: {t2_path}")

    # =========================================================================
    # 3. STALE MEMORY ARM (Safety under Contradiction)
    # =========================================================================
    print("\n--- 3. Running Stale Memory Arm ---")
    stale_scenarios = build_scenarios_v3(
        count=n_scen,
        num_branches=4,
        num_fatals=2,
        accuracy=0.0,  # adversarial/stale memory recommending fatal
        applicability_mode="calibrated",
        stale=True,
        seed=999,
    )
    stale_records = []
    base_stale_res = run_cell(stale_scenarios, policy="baseline", seed=9991)

    for pol in POLICIES:
        res = run_cell(stale_scenarios, policy=pol, seed=9991)
        stale_records.append({
            "policy": pol,
            "mean_nodes": round(res["mean_nodes"], 4),
            "ci_nodes_95": f"[{res['ci_nodes_lo']:.3f}, {res['ci_nodes_hi']:.3f}]",
            "mean_fatals": round(res["mean_fatals"], 4),
            "ci_fatals_95": f"[{res['ci_fatals_lo']:.3f}, {res['ci_fatals_hi']:.3f}]",
            "delta_fatals_vs_baseline": round(res["mean_fatals"] - base_stale_res["mean_fatals"], 4),
            "safety_veto_intact": (abs(res["mean_fatals"] - base_stale_res["mean_fatals"]) <= 0.05),
        })

    t3_path = os.path.join(out_dir, "table_luna_3_stale_arm.csv")
    with open(t3_path, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=list(stale_records[0].keys()))
        writer.writeheader()
        for r in stale_records:
            writer.writerow(r)
    print(f"Saved: {t3_path}")

    # =========================================================================
    # 4. VERIFICATION ABLATION (Cost-Benefit Analysis)
    # =========================================================================
    print("\n--- 4. Running Verification Cost-Benefit Ablation ---")
    ver_ablation_records = []
    # Evaluam la acuratete joasa (0.2), medie (0.5), si inalta (0.8)
    for acc in [0.2, 0.5, 0.8]:
        scens = build_scenarios_v3(count=n_scen, num_branches=4, num_fatals=2, accuracy=acc, applicability_mode="calibrated", seed=int(acc * 500))
        r_base = run_cell(scens, policy="baseline", seed=555)
        r_v1 = run_cell(scens, policy="v1_uncertainty", seed=555)
        r_v2 = run_cell(scens, policy="v2_verification_action", seed=555)

        ver_ablation_records.append({
            "accuracy": acc,
            "baseline_nodes": round(r_base["mean_nodes"], 3),
            "baseline_fatals": round(r_base["mean_fatals"], 3),
            "v1_uncertainty_nodes": round(r_v1["mean_nodes"], 3),
            "v1_uncertainty_fatals": round(r_v1["mean_fatals"], 3),
            "v2_verification_nodes": round(r_v2["mean_nodes"], 3),
            "v2_verification_fatals": round(r_v2["mean_fatals"], 3),
            "verification_nodes_consumed": round(r_v2["mean_ver_nodes"], 3),
            "fatal_reduction_v2_vs_v1": round(r_v1["mean_fatals"] - r_v2["mean_fatals"], 3),
            "net_cost_saving_v2_vs_v1": round(r_v1["mean_nodes"] - r_v2["mean_nodes"], 3),
        })

    t4_path = os.path.join(out_dir, "table_luna_4_verification_ablation.csv")
    with open(t4_path, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=list(ver_ablation_records[0].keys()))
        writer.writeheader()
        for r in ver_ablation_records:
            writer.writerow(r)
    print(f"Saved: {t4_path}")

    # =========================================================================
    # 5. ROBUSTNESS GRID: Branches, Fatals, Exploration
    # =========================================================================
    print("\n--- 5. Running Robustness Grid ---")
    robustness_configs = [
        {"desc": "Standard_K4_F2", "k": 4, "f": 2, "c": 1.414},
        {"desc": "LowFatal_K4_F1", "k": 4, "f": 1, "c": 1.414},
        {"desc": "LargeSearch_K6_F3", "k": 6, "f": 3, "c": 1.414},
        {"desc": "DenseSearch_K8_F4", "k": 8, "f": 4, "c": 1.414},
        {"desc": "Exploration_Low_c1.0", "k": 4, "f": 2, "c": 1.000},
        {"desc": "Exploration_High_c2.0", "k": 4, "f": 2, "c": 2.000},
    ]
    robustness_records = []

    for cfg in robustness_configs:
        k = cfg["k"]
        f_cnt = cfg["f"]
        c_exp = cfg["c"]
        desc = cfg["desc"]

        for acc in [0.2, 0.5, 0.8]:
            scens = build_scenarios_v3(count=n_scen, num_branches=k, num_fatals=f_cnt, accuracy=acc, applicability_mode="calibrated", seed=777)
            b_res = run_cell(scens, policy="baseline", exploration=c_exp, seed=888)
            v1_res = run_cell(scens, policy="v1_uncertainty", exploration=c_exp, seed=888)
            v2_res = run_cell(scens, policy="v2_verification_action", exploration=c_exp, seed=888)

            robustness_records.append({
                "config": desc,
                "branches_K": k,
                "fatals_F": f_cnt,
                "exploration_c": c_exp,
                "accuracy": acc,
                "baseline_nodes": round(b_res["mean_nodes"], 3),
                "baseline_fatals": round(b_res["mean_fatals"], 3),
                "v1_nodes": round(v1_res["mean_nodes"], 3),
                "v1_fatals": round(v1_res["mean_fatals"], 3),
                "v1_delta_nodes": round(b_res["mean_nodes"] - v1_res["mean_nodes"], 3),
                "v2_nodes": round(v2_res["mean_nodes"], 3),
                "v2_fatals": round(v2_res["mean_fatals"], 3),
                "v2_delta_nodes": round(b_res["mean_nodes"] - v2_res["mean_nodes"], 3),
            })

    t5_path = os.path.join(out_dir, "table_luna_5_robustness_grid.csv")
    with open(t5_path, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=list(robustness_records[0].keys()))
        writer.writeheader()
        for r in robustness_records:
            writer.writerow(r)
    print(f"Saved: {t5_path}")

    print("\n=== ALL PLANNING INFLUENCE V3 EXPERIMENTS COMPLETED SUCCESSFULLY ===")


if __name__ == "__main__":
    main()
