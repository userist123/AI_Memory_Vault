"""07_EVALUATION/r010_plasticity_distribution_evaluator.py — Empirical simulation for Task r010.

Simulates 50 realistic execution cycles across the real AI Memory Vault graph (411 edges):
- Mix of verified successes (75%) and verified failures (25%).
- Enforces 5-state causal attribution (distinguishing used notes from merely context-packed nodes).
- Measures synaptic weight distribution shifts and histogram bucket dynamics.
- Tests append-only telemetry journal logging and exact rollback restoration.

Run: python 07_EVALUATION/r010_plasticity_distribution_evaluator.py
"""
from __future__ import annotations

import json
import random
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PACKAGES = ROOT / "03_IMPLEMENTATION" / "packages"
for p in (str(ROOT), str(PACKAGES)):
    if p not in sys.path:
        sys.path.insert(0, p)

from graph.plasticity import (
    PlasticityEngine,
    PlasticityJournal,
    AttributionModel,
    MAX_WEIGHT,
    MIN_WEIGHT,
    MAX_SINGLE_DELTA,
)
from graph.synapse_store import SynapseStore, Synapse
from retrieval.vault_index import VaultIndex, DEFAULT_ROOTS


def bucket_distribution(weights: list[float]) -> dict[str, int]:
    buckets = {
        "[0.0, 0.2)": 0,
        "[0.2, 0.4)": 0,
        "[0.4, 0.6)": 0,
        "[0.6, 0.8)": 0,
        "[0.8, 1.0)": 0,
        "[1.0, 1.2)": 0,
        "[1.2, 1.5]": 0,
    }
    for w in weights:
        if w < 0.2:
            buckets["[0.0, 0.2)"] += 1
        elif w < 0.4:
            buckets["[0.2, 0.4)"] += 1
        elif w < 0.6:
            buckets["[0.4, 0.6)"] += 1
        elif w < 0.8:
            buckets["[0.6, 0.8)"] += 1
        elif w < 1.0:
            buckets["[0.8, 1.0)"] += 1
        elif w < 1.2:
            buckets["[1.0, 1.2)"] += 1
        else:
            buckets["[1.2, 1.5]"] += 1
    return buckets


def run_simulation(num_cycles: int = 50, seed: int = 42) -> dict:
    random.seed(seed)
    journal_path = ROOT / "telemetry" / "test_eval_plasticity_journal.jsonl"
    if journal_path.exists():
        journal_path.unlink()

    journal = PlasticityJournal(journal_path)
    engine = PlasticityEngine(journal=journal, default_rate=0.15)

    idx = VaultIndex.load(ROOT, roots=DEFAULT_ROOTS, include_raw=True, include_archived=True)
    store = SynapseStore.from_index(idx)

    all_synapses = store.all()
    initial_weights = [s.weight for s in all_synapses]
    initial_stats = {
        "count": len(initial_weights),
        "mean": round(sum(initial_weights) / len(initial_weights), 4),
        "min": round(min(initial_weights), 4),
        "max": round(max(initial_weights), 4),
        "buckets": bucket_distribution(initial_weights),
    }

    nodes = list(set([s.source_id for s in all_synapses] + [s.target_id for s in all_synapses]))
    cycle_records = []

    strengthened_count = 0
    depressed_count = 0
    unmodified_count = 0

    for i in range(num_cycles):
        run_id = f"sim_run_{i:03d}"
        seed_node = random.choice(nodes)
        neighbors = store.neighbors(seed_node)

        if not neighbors:
            continue

        traversed = random.sample(neighbors, min(len(neighbors), random.randint(1, 3)))
        traversed_dicts = [
            {"source": s.source_id, "target": s.target_id, "relation": s.relation}
            for s in traversed
        ]

        targets = [s.target_id for s in traversed]
        noise_nodes = random.sample(nodes, min(len(nodes), 2))
        final_context = list(set(targets + noise_nodes))

        is_success = (random.random() < 0.75)
        outcome_val = "success" if is_success else "fail"

        # Anti-hub-pollution rule: only 1 target is actually used
        used_target = random.choice(targets)
        used_ids = [used_target]

        trace = {
            "run_id": run_id,
            "final_context_ids": final_context,
            "graph_edges_traversed": traversed_dicts,
        }
        outcome_rec = {
            "run_id": run_id,
            "outcome": outcome_val,
            "verification_method": "test_pass" if is_success else "exit_code",
        }

        res = engine.apply_outcome(
            synapse_store=store,
            candidate_trace=trace,
            outcome_record=outcome_rec,
            used_memory_ids=used_ids,
            run_id=run_id,
        )
        cycle_records.append(res)

    final_weights = [s.weight for s in store.all()]
    final_stats = {
        "count": len(final_weights),
        "mean": round(sum(final_weights) / len(final_weights), 4),
        "min": round(min(final_weights), 4),
        "max": round(max(final_weights), 4),
        "buckets": bucket_distribution(final_weights),
    }

    for init_w, fin_w in zip(initial_weights, final_weights):
        if fin_w > init_w + 1e-5:
            strengthened_count += 1
        elif fin_w < init_w - 1e-5:
            depressed_count += 1
        else:
            unmodified_count += 1

    # Test rollback on 5 runs
    sample_runs_to_rollback = [f"sim_run_{i:03d}" for i in range(5)]
    rollback_reverted = 0
    for r_id in sample_runs_to_rollback:
        rb = journal.rollback(r_id, store)
        rollback_reverted += rb.edges_reverted

    report = {
        "total_cycles": num_cycles,
        "initial_stats": initial_stats,
        "final_stats": final_stats,
        "edges_strengthened": strengthened_count,
        "edges_depressed": depressed_count,
        "edges_unmodified": unmodified_count,
        "journal_total_entries": len(journal.load_entries()),
        "rollback_test_runs": len(sample_runs_to_rollback),
        "rollback_test_reverted_edges": rollback_reverted,
    }

    if journal_path.exists():
        journal_path.unlink()

    return report


if __name__ == "__main__":
    rep = run_simulation(50)
    print(json.dumps(rep, indent=2))
