"""
simulate_puct_benchmark.py — Empirical PUCT Baseline Benchmark Simulation

Derives the true expected search cost under uninformed baseline PUCT
for K=4 branches (1 optimal, 1 suboptimal reward=0.25, 2 fatal reward=-1.0),
exploration c_puct=1.414, and orthogonal SHA-256 tie-breaking.

Pre-registered in PLANNING_INFLUENCE_PREREGISTRATION_V2_1.md to replace
the naive non-replacement formula E[N] = (K+1)/2 = 2.50.
"""
import argparse
import os
import sys
from typing import Tuple
import numpy as np

# Ensure root is in sys.path
sys.path.insert(0, os.path.abspath("."))
from importlib import import_module

v3_mod = import_module("07_EVALUATION.luna.planning_influence_mve_v3")
build_scenarios_v3 = v3_mod.build_scenarios_v3
run_planner_v3 = v3_mod.run_planner_v3

# Canonical pre-registered simulation benchmark (N=200,000, seed=2026)
BENCHMARK_MEAN: float = 2.9206
BENCHMARK_STD: float = 1.4397
BENCHMARK_SE: float = 0.003219


def simulate_puct_benchmark(
    n_episodes: int = 200000,
    seed: int = 2026,
) -> Tuple[float, float, float]:
    """
    Runs deterministic Monte Carlo simulation of PUCT search dynamics
    with uniform priors over K=4 branches.
    Returns (mean_nodes, std_nodes, standard_error).
    """
    scenarios = build_scenarios_v3(
        count=n_episodes,
        num_branches=4,
        num_fatals=2,
        seed=seed,
    )
    counts = [
        run_planner_v3(sc, policy="baseline", seed=seed + i).node_visits
        for i, sc in enumerate(scenarios)
    ]
    arr = np.array(counts, dtype=np.float64)
    m = float(np.mean(arr))
    s = float(np.std(arr))
    se = float(s / np.sqrt(len(arr)))
    return m, s, se


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Simulate PUCT Baseline Benchmark")
    parser.add_argument("--episodes", type=int, default=200000, help="Number of episodes")
    parser.add_argument("--seed", type=int, default=2026, help="Random seed")
    args = parser.parse_args()

    print(f"Simulating PUCT baseline across {args.episodes} episodes (seed={args.seed})...")
    mean_val, std_val, se_val = simulate_puct_benchmark(args.episodes, args.seed)
    print(f"Benchmark Result: mean={mean_val:.4f}, std={std_val:.4f}, SE={se_val:.6f}")
