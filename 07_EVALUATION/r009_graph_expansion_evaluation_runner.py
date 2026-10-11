"""scratch/run_r009_evaluation.py -- Measure retrieval performance before vs after graph expansion.

Evaluates:
- 10 core regression queries (D01-D10 from dev.json)
- Precision@5, MRR (Mean Reciprocal Rank)
- Latency (mean, p95)
- final_context_ids comparison: which queries changed, nodes expanded
"""
from __future__ import annotations

import json
import os
import sys
import time
from pathlib import Path
from typing import Any, Dict, List

vault_root = Path(r"c:\Users\Marius\Documents\Codex\AI_Memory_Vault_CODEX_READY")
sys.path.insert(0, str(vault_root / "03_IMPLEMENTATION" / "packages"))

os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "test_hmac_secret_for_eval_harness_32bytes_long")

from retrieval.vault_index import VaultIndex, DEFAULT_ROOTS
from memory_controller.controller import MemoryController, StorageEngine, Lifecycle
from memory_controller.authorizer import Principal


def load_real_vault_storage(index: VaultIndex) -> StorageEngine:
    storage = StorageEngine()
    for note in index.notes:
        storage.set(note.id, {
            "id": note.id,
            "title": note.title,
            "type": note.type,
            "category": getattr(note, "category", "general"),
            "lifecycle": note.lifecycle,
            "confidence": getattr(note, "confidence", "high"),
            "verification": getattr(note, "verification", "verified"),
            "tags": list(note.tags),
            "content": note.text,
        })
    return storage


def evaluate():
    dev_path = vault_root / "07_EVALUATION" / "heldout_retrieval_benchmark_v1" / "dev.json"
    with open(dev_path, encoding="utf-8") as f:
        dev_data = json.load(f)

    cases = [c for c in dev_data["cases"] if not c.get("abstain")]
    print(f"Loaded {len(cases)} answerable dev queries:")

    # Load canonical index and storage
    idx = VaultIndex.load(vault_root, roots=DEFAULT_ROOTS, lifecycles=("ACTIVE", "REVIEW", "NORMALIZED", "CLASSIFIED", "NONE"), include_raw=False, include_archived=False)
    print(f"Loaded index: {len(idx.notes)} notes")

    storage = load_real_vault_storage(idx)
    print(f"Loaded storage: {len(storage.store)} notes")

    # Map gold note IDs to indexed IDs if needed
    resolved_golds = {}
    for c in cases:
        c_id = c["id"]
        golds = c["gold_relevant_notes"]
        mapped = []
        for g in golds:
            if g in idx.by_id:
                mapped.append(g)
            elif "agents" in g:
                for nid, n in idx.by_id.items():
                    if "agents" in n.title.lower() or "agents.md" in str(n.path).lower():
                        mapped.append(nid)
                        break
            elif "cognitive" in g or "rules" in g:
                for nid, n in idx.by_id.items():
                    if "cognitive" in n.title.lower() or "rules" in str(n.path).lower():
                        mapped.append(nid)
                        break
            else:
                mapped.append(g)
        resolved_golds[c_id] = mapped
        print(f"Query {c_id}: {c['query'][:50]}... -> Gold: {mapped}")

    # Condition 1: Expansion OFF
    ctrl_off = MemoryController(storage, index=idx, enable_graph_expansion=False)
    # Condition 2: Expansion ON
    ctrl_on = MemoryController(storage, index=idx, enable_graph_expansion=True)

    latencies_off = []
    latencies_on = []
    results_off = {}
    results_on = {}

    # Warmup
    ctrl_off.search(Principal.AI_AGENT, "warmup test", page_size=5)
    ctrl_on.search(Principal.AI_AGENT, "warmup test", page_size=5)

    # Run Condition OFF (5 iterations for stable latency)
    for c in cases:
        for _ in range(4):
            ctrl_off.search(Principal.AI_AGENT, c["query"], page_size=5)
        t0 = time.perf_counter()
        pack = ctrl_off.search(Principal.AI_AGENT, c["query"], page_size=5)
        dt = (time.perf_counter() - t0) * 1000
        latencies_off.append(dt)
        res_ids = [r["id"] for r in pack.get("results", [])]
        results_off[c["id"]] = {
            "results": res_ids,
            "trace": pack.get("candidate_trace", {}),
            "latency_ms": dt,
        }

    # Run Condition ON (5 iterations for stable latency)
    for c in cases:
        for _ in range(4):
            ctrl_on.search(Principal.AI_AGENT, c["query"], page_size=5)
        t0 = time.perf_counter()
        pack = ctrl_on.search(Principal.AI_AGENT, c["query"], page_size=5)
        dt = (time.perf_counter() - t0) * 1000
        latencies_on.append(dt)
        res_ids = [r["id"] for r in pack.get("results", [])]
        results_on[c["id"]] = {
            "results": res_ids,
            "trace": pack.get("candidate_trace", {}),
            "latency_ms": dt,
        }

    # Compute Metrics
    def compute_metrics(run_results):
        precisions = []
        recip_ranks = []
        for c in cases:
            cid = c["id"]
            gold = set(resolved_golds.get(cid, []))
            retrieved = run_results[cid]["results"][:5]

            # Precision@5
            if retrieved and gold:
                hits = sum(1 for rid in retrieved if rid in gold)
                precisions.append(hits / min(len(retrieved), 5))
            else:
                precisions.append(0.0)

            # MRR
            rr = 0.0
            for rank, rid in enumerate(retrieved, start=1):
                if rid in gold:
                    rr = 1.0 / rank
                    break
            recip_ranks.append(rr)

        avg_p5 = sum(precisions) / len(precisions) if precisions else 0.0
        mrr = sum(recip_ranks) / len(recip_ranks) if recip_ranks else 0.0
        return avg_p5, mrr

    p5_off, mrr_off = compute_metrics(results_off)
    p5_on, mrr_on = compute_metrics(results_on)

    latencies_off.sort()
    latencies_on.sort()
    p95_off = latencies_off[int(len(latencies_off) * 0.95)]
    p95_on = latencies_on[int(len(latencies_on) * 0.95)]
    p95_delta = p95_on - p95_off

    print("\n================== EVALUATION REPORT ==================")
    print(f"Condition OFF: Precision@5 = {p5_off:.4f}, MRR = {mrr_off:.4f}, p95 Latency = {p95_off:.2f}ms")
    print(f"Condition ON:  Precision@5 = {p5_on:.4f}, MRR = {mrr_on:.4f}, p95 Latency = {p95_on:.2f}ms")
    print(f"p95 Latency Delta: {p95_delta:+.2f}ms (Constraint: <= +5.0ms)")

    # Per-query diff analysis
    print("\n--- Per-Query Comparison ---")
    query_diffs = []
    for c in cases:
        cid = c["id"]
        off_ids = results_off[cid]["results"]
        on_ids = results_on[cid]["results"]
        trace_on = results_on[cid]["trace"]
        expanded = trace_on.get("graph_expanded_ids", [])
        hub_skipped = trace_on.get("graph_hub_nodes_skipped", [])
        changed = off_ids != on_ids

        diff_info = {
            "id": cid,
            "query": c["query"],
            "class": c["class"],
            "off_final_context_ids": off_ids,
            "on_final_context_ids": on_ids,
            "changed": changed,
            "graph_expanded_ids": expanded,
            "graph_hub_nodes_skipped": hub_skipped,
        }
        query_diffs.append(diff_info)
        print(f"[{cid}] ({c['class']}): changed={changed}, expanded={len(expanded)}, hub_skipped={len(hub_skipped)}")
        if changed:
            print(f"   OFF: {off_ids}")
            print(f"   ON:  {on_ids}")
            print(f"   Expanded: {expanded}")

    # Output full report
    report_data = {
        "metrics": {
            "expansion_off": {"precision_at_5": p5_off, "mrr": mrr_off, "p95_latency_ms": p95_off},
            "expansion_on": {"precision_at_5": p5_on, "mrr": mrr_on, "p95_latency_ms": p95_on},
            "p95_latency_delta_ms": p95_delta,
            "p95_latency_constraint_passed": p95_delta <= 5.0,
        },
        "query_diffs": query_diffs,
    }

    out_file = vault_root / "07_EVALUATION" / "r009_graph_expansion_eval.json"
    with open(out_file, "w", encoding="utf-8") as f:
        json.dump(report_data, f, indent=2)
    print(f"\nSaved raw eval data to {out_file}")


if __name__ == "__main__":
    evaluate()
