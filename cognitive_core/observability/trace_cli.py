import argparse
import json
import sys
from pathlib import Path
from memory_controller.controller import MemoryController
from memory_controller.storage import FileStorageEngine
from cognitive_core.observability.retrieval_tracer import RetrievalTracer
from cognitive_core.observability.ab_comparison_engine import ABComparisonEngine
from cognitive_core.observability.memory_outcome_tracer import MemoryOutcomeTracer

BASE_DIR = Path.cwd()

def main():
    parser = argparse.ArgumentParser(description="Antigravity Developer Observability CLI (R001)")
    parser.add_argument("--query", "-q", type=str, help="Interogare de regăsit prin pipeline")
    parser.add_argument("--ab-activation", action="store_true", help="Rulează benchmark A/B Base vs Base+Activation")
    parser.add_argument("--outcomes", action="store_true", help="Scanează execuțiile pentru corelația Memory-Use -> Outcome")
    parser.add_argument("--json", action="store_true", help="Ieșire JSON brută")

    args = parser.parse_args()

    if args.ab_activation:
        engine = ABComparisonEngine()
        sample_notes = [
            {"id": "M-ADAPT-001", "content": "Prompting, retrieval, fine-tuning, alignment levers"},
            {"id": "M-ARCH-001", "content": "AI applications are layered systems: capabilities, context, tools"},
            {"id": "M-DISTRIBUTED-001", "content": "Distributed systems introduce timing and consistency challenges"},
            {"id": "M-TOOLS-001", "content": "External tools extend models, auth and latencies are dependencies"}
        ]
        histories = {"M-TOOLS-001": 0.85, "M-ARCH-001": 0.50}
        q = args.query or "What adaptation and tooling levers exist?"
        res = engine.compare_base_vs_activation(q, sample_notes, histories)
        if args.json:
            print(json.dumps(res.to_dict(), indent=2))
        else:
            print(f"=== A/B ACTIVATION COMPARISON: '{q}' ===")
            print(f"Top-1 Base: {res.top1_a} | Top-1 Activation: {res.top1_b} | Flipped: {res.top1_flipped}")
            print(f"Kendall Tau: {res.kendall_tau:.4f} | Spearman Rho: {res.spearman_rho:.4f}")
            print(f"Mean Rank Delta: {res.mean_absolute_rank_delta:.2f}")
            for it in res.items:
                print(f"  [{it.note_id}] Base Rank {it.base_rank} -> Treat Rank {it.treatment_rank} (Delta: {it.rank_delta:+d}, Act: {it.activation_boost:.2f})")
        return

    if args.outcomes:
        tracer = MemoryOutcomeTracer()
        linkages = tracer.scan_traces()
        if args.json:
            print(json.dumps([l.to_dict() for l in linkages], indent=2))
        else:
            print(f"=== MEMORY-USE TO OUTCOME LINKAGE SCAN ({len(linkages)} linkages found) ===")
            tier_counts = {}
            for l in linkages:
                tier_counts[l.utility_tier.value] = tier_counts.get(l.utility_tier.value, 0) + 1
            for t, cnt in tier_counts.items():
                print(f"  {t}: {cnt}")
            if linkages:
                print(f"Sample Linkage: {linkages[0].to_dict()}")
        return

    if args.query:
        storage = FileStorageEngine(vault_root=str(BASE_DIR))
        controller = MemoryController(storage=storage)
        tracer = RetrievalTracer()
        trace = tracer.trace(args.query, controller)
        if args.json:
            print(trace.to_json())
        else:
            print(trace.to_markdown_table())
        return

    parser.print_help()

if __name__ == "__main__":
    main()
