"""07_EVALUATION/r011_attention_wiring_evaluation.py — Precondition audit and empirical evaluation of AttentionModel wiring.

Evaluates whether wiring memory/attention.py into MemoryController.search() produces measurable gains
or represents ungrounded theatre over a candidate set that graph expansion did not improve.
"""
from __future__ import annotations

import json
import os
import sys
import time
from pathlib import Path
from typing import Any, Dict, List, Tuple

ROOT = Path(__file__).resolve().parents[1]
PACKAGES = ROOT / "03_IMPLEMENTATION" / "packages"
for p in (str(ROOT), str(PACKAGES)):
    if p not in sys.path:
        sys.path.insert(0, p)

os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "test_hmac_secret_for_eval_harness_32bytes_long")

from retrieval.vault_index import VaultIndex, DEFAULT_ROOTS
from memory_controller.controller import MemoryController, StorageEngine, Lifecycle
from memory_controller.authorizer import Principal
from cognitive_core.attention import AttentionModel


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


def evaluate() -> Dict[str, Any]:
    dev_path = ROOT / "07_EVALUATION" / "heldout_retrieval_benchmark_v1" / "dev.json"
    with open(dev_path, encoding="utf-8") as f:
        dev_cases = json.load(f)["cases"]

    idx = VaultIndex.load(
        ROOT,
        roots=DEFAULT_ROOTS,
        lifecycles=("ACTIVE", "REVIEW", "NORMALIZED", "CLASSIFIED", "NONE"),
        include_raw=False,
        include_archived=False,
    )
    storage = load_real_vault_storage(idx)
    controller = MemoryController(storage, index=idx, enable_graph_expansion=False)
    attention = AttentionModel(activation_weight=0.5, confidence_weight=0.3, recency_weight=0.2)

    print("=" * 80)
    print("TASK r011: COGNITIVE MODULE WIRING AUDIT — ATTENTION MODEL EVALUATION")
    print("=" * 80)

    # Build title-to-id mapping for resolving gold relevant notes
    title_to_id = {}
    for nid, note in storage.store.items():
        title_to_id[note.get("title", "").strip().lower()] = nid
        title_to_id[nid.strip().lower()] = nid

    baseline_p5 = []
    attention_p5 = []
    baseline_rr = []
    attention_rr = []
    total_eval = 0
    query_details = []

    for c in dev_cases:
        if c.get("abstain"):
            continue

        q_id = c["id"]
        q_class = c["class"]
        query = c["query"]
        raw_gold = c["gold_relevant_notes"]
        gold_ids = set()
        for g in raw_gold:
            gl = g.strip().lower()
            if gl in title_to_id:
                gold_ids.add(title_to_id[gl])
            else:
                gold_ids.add(g)

        required_facts = [f.lower() for f in c.get("required_facts", [])]
        total_eval += 1

        # 1. Baseline Search
        t0 = time.perf_counter()
        base_pack = controller.search(Principal.AI_AGENT, query=query, page_size=5)
        base_lat = time.perf_counter() - t0
        base_results = base_pack.get("results", [])
        base_ids = [r.get("id") for r in base_results]

        # Precision@5 & RR
        base_hits = [rid for rid in base_ids if rid in gold_ids]
        base_p5_score = len(base_hits) / 5.0
        base_rank = next((i + 1 for i, rid in enumerate(base_ids) if rid in gold_ids), 0)
        base_recip_rank = 1.0 / base_rank if base_rank > 0 else 0.0

        baseline_p5.append(base_p5_score)
        baseline_rr.append(base_recip_rank)

        # 2. Attention-Scored Re-ranking
        # AttentionModel computes:
        # total_score = (activation * activation_weight) + (conf_score * confidence_weight) + (recency_score * recency_weight)
        # We score each candidate in base_results and re-rank
        t0 = time.perf_counter()
        scored_candidates = []
        for r in base_results:
            raw_score = float(r.get("score", 1.0))
            norm_act = min(1.0, max(0.0, raw_score / 10.0 if raw_score > 1.0 else raw_score))
            att_score = attention.calculate_score(
                node=r,
                activation=norm_act,
                recency_tick=0,
                current_tick=0,
            )
            scored_candidates.append((att_score, r))

        scored_candidates.sort(key=lambda x: -x[0])
        att_lat = time.perf_counter() - t0
        attention_results = [item[1] for item in scored_candidates]
        att_ids = [r.get("id") for r in attention_results]

        att_hits = [rid for rid in att_ids if rid in gold_ids]
        att_p5_score = len(att_hits) / 5.0
        att_rank = next((i + 1 for i, rid in enumerate(att_ids) if rid in gold_ids), 0)
        att_recip_rank = 1.0 / att_rank if att_rank > 0 else 0.0

        attention_p5.append(att_p5_score)
        attention_rr.append(att_recip_rank)

        # Fact checking
        top_base_content = base_results[0].get("content", "").lower() if base_results else ""
        top_att_content = attention_results[0].get("content", "").lower() if attention_results else ""
        base_facts_found = sum(1 for f in required_facts if f in top_base_content)
        att_facts_found = sum(1 for f in required_facts if f in top_att_content)

        order_changed = base_ids != att_ids

        detail = {
            "id": q_id,
            "class": q_class,
            "query": query,
            "gold_ids": list(gold_ids),
            "baseline_ids": base_ids,
            "attention_ids": att_ids,
            "baseline_p5": base_p5_score,
            "attention_p5": att_p5_score,
            "baseline_mrr": base_recip_rank,
            "attention_mrr": att_recip_rank,
            "order_changed": order_changed,
            "base_facts_found": base_facts_found,
            "att_facts_found": att_facts_found,
            "total_facts": len(required_facts),
        }
        query_details.append(detail)

        print(f"Query {q_id} [{q_class}]:")
        print(f"  Gold notes: {list(gold_ids)}")
        print(f"  Base Top-2: {base_ids[:2]} (P@5: {base_p5_score:.2f}, RR: {base_recip_rank:.2f})")
        print(f"  Attn Top-2: {att_ids[:2]} (P@5: {att_p5_score:.2f}, RR: {att_recip_rank:.2f})")
        print(f"  Re-ordered: {order_changed}")
        print("-" * 60)

    mean_base_p5 = sum(baseline_p5) / len(baseline_p5) if baseline_p5 else 0.0
    mean_att_p5 = sum(attention_p5) / len(attention_p5) if attention_p5 else 0.0
    mean_base_mrr = sum(baseline_rr) / len(baseline_rr) if baseline_rr else 0.0
    mean_att_mrr = sum(attention_rr) / len(attention_rr) if attention_rr else 0.0

    print("\n" + "=" * 80)
    print("AGGREGATE BENCHMARK RESULTS:")
    print("=" * 80)
    print(f"  Total answerable queries: {total_eval}")
    print(f"  Baseline Precision@5:     {mean_base_p5:.4f}")
    print(f"  Attention Precision@5:    {mean_att_p5:.4f} (Delta: {mean_att_p5 - mean_base_p5:+.4f})")
    print(f"  Baseline MRR:             {mean_base_mrr:.4f}")
    print(f"  Attention MRR:            {mean_att_mrr:.4f} (Delta: {mean_att_mrr - mean_base_mrr:+.4f})")
    queries_changed = sum(1 for d in query_details if d["order_changed"])
    print(f"  Queries with order change:{queries_changed}/{total_eval}")
    print("=" * 80)

    summary = {
        "total_queries": total_eval,
        "mean_baseline_p5": mean_base_p5,
        "mean_attention_p5": mean_att_p5,
        "delta_p5": mean_att_p5 - mean_base_p5,
        "mean_baseline_mrr": mean_base_mrr,
        "mean_attention_mrr": mean_att_mrr,
        "delta_mrr": mean_att_mrr - mean_base_mrr,
        "queries_reordered": queries_changed,
        "details": query_details,
    }

    out_path = ROOT / "07_EVALUATION" / "r011_attention_wiring_eval.json"
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(summary, f, indent=2)
    print(f"Detailed JSON output written to: {out_path}")
    return summary


if __name__ == "__main__":
    evaluate()

