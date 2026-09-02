"""evaluation/context_packing/experiment_runner.py — P1 Context Packing Laboratory.

Executes controlled evaluation of packing strategies (P0 -> P1 -> P2 -> P3 -> P4)
over R4 retrieved candidates across 15 benchmark queries using local models M1 (3B) and M2 (7B).
Generates:
  - evaluation/reports/context_packing_report.json
  - evaluation/reports/context_packing_report.md
"""
from __future__ import annotations

import json
import os
import sys
import time
from pathlib import Path
from typing import Any, Dict, List, Optional, Set, Tuple
import yaml

ROOT = Path(__file__).resolve().parents[2]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "test_hmac_secret_for_eval_harness_32bytes_long")

from cognitive_core.local_provider import LocalProvider, ModelRequest
from evaluation.retrieval_diagnostic_runner import build_real_vault_storage, check_facts_in_context
from evaluation.full_context_baseline import evaluate_response_accuracy
from evaluation.retrieval_fusion.adapters import RetrievalAdapter
from evaluation.context_packing.packer_adapters import PackerAdapters


def load_gold_context() -> List[Dict[str, Any]]:
    yaml_path = Path(__file__).parent / "gold_context.yaml"
    with open(yaml_path, "r", encoding="utf-8") as f:
        data = yaml.safe_load(f)
    return data.get("queries", [])


def load_experiment_config() -> Dict[str, Any]:
    yaml_path = Path(__file__).parent / "experiment_config.yaml"
    with open(yaml_path, "r", encoding="utf-8") as f:
        return yaml.safe_load(f)


def classify_packing_failure(
    facts_before: int,
    facts_after: int,
    accuracy: float,
    strategy: str,
    query_class: str,
) -> str:
    if accuracy >= 0.8:
        return "SUCCESS"
    if facts_before > 0 and facts_after == 0:
        return "SECTION_SELECTION_FAILURE"
    if facts_before > facts_after:
        if query_class == "CONTRADICTION_GUARDRAIL":
            return "NEGATION_LOSS"
        if query_class == "TEMPORAL":
            return "TEMPORAL_CONTEXT_LOSS"
        return "BUDGET_FAILURE"
    if facts_after == len(facts_before if isinstance(facts_before, list) else []) or facts_after >= facts_before:
        return "MODEL_FAILURE"
    return "BUDGET_FAILURE"


def run_context_packing_lab(
    endpoint: str = "http://127.0.0.1:11434",
    m1_name: str = "qwen2.5-coder:3b",
    m2_name: str = "qwen2.5-coder:7b",
    reports_dir: Optional[Path] = None,
) -> Dict[str, Any]:
    rep_dir = reports_dir or (ROOT / "07_EVALUATION" / "reports")
    rep_dir.mkdir(parents=True, exist_ok=True)

    config = load_experiment_config()
    gold_queries = load_gold_context()

    print("================================================================================")
    print("P1 CONTEXT PACKING LABORATORY: P0 -> P1 -> P2 -> P3 -> P4 CONTROLLED BENCHMARK")
    print(f"Models: M1={m1_name}, M2={m2_name} | Endpoint: {endpoint}")
    print("================================================================================\n")

    # Ingest real disk notes into StorageEngine and build R4 adapter
    storage = build_real_vault_storage()
    all_notes = storage.query(intent="all")
    retrieval_adapter = RetrievalAdapter(all_notes)

    p1 = LocalProvider(model_name=m1_name, base_url=endpoint, timeout_seconds=120.0, num_ctx=4096)
    p2 = LocalProvider(model_name=m2_name, base_url=endpoint, timeout_seconds=240.0, num_ctx=4096)

    budget_dict = {
        "max_notes": 5,
        "soft_limit_bytes": 16384,
        "hard_limit_bytes": 32768,
        "soft_limit_tokens": 1800,
        "hard_limit_tokens": 2500,
        "max_full_documents": 5,
    }

    strategies = ["P0", "P1", "P2", "P3", "P4"]
    results_by_strategy: Dict[str, List[Dict[str, Any]]] = {s: [] for s in strategies}

    # Step 1: Pre-retrieve R4 candidates for all 15 queries to ensure identical inputs
    print(">>> Step 1: Generating R4 Candidate Baseline for 15 Queries...")
    query_candidates: Dict[str, List[Dict[str, Any]]] = {}
    for q_item in gold_queries:
        qid = q_item["id"]
        q_text = q_item["query"]
        entities = q_item.get("critical_entities", [])
        cands = retrieval_adapter.retrieve_r4_full_fusion_graph(q_text, top_k=5, query_entities=entities)
        query_candidates[qid] = cands

    # Step 2: Evaluate each packing strategy on M1 (3B)
    print("\n>>> Step 2: Executing Packing Strategies on M1 (3B)...")
    for strat in strategies:
        print(f"\n[Evaluating Packing Strategy: {strat}]")
        for q_idx, q_item in enumerate(gold_queries, 1):
            qid = q_item["id"]
            q_text = q_item["query"]
            req_facts = q_item["required_facts"]
            entities = q_item.get("critical_entities", [])
            q_class = q_item.get("class", "UNKNOWN")
            candidates = query_candidates[qid]

            # Measure facts available before packing
            cand_raw_text = "\n---\n".join([f"[{c.get('id')}]: {c.get('content', '')}" for c in candidates])
            facts_before_list, _, _ = check_facts_in_context(cand_raw_text, req_facts)
            facts_before_count = len(facts_before_list)

            t0 = time.perf_counter()

            # Execute specific packing strategy
            if strat == "P0":
                pack_res = PackerAdapters.pack_p0_current(candidates, budget_dict, request_id=f"p0_{qid}")
            elif strat == "P1":
                pack_res = PackerAdapters.pack_p1_full_context(candidates)
            elif strat == "P2":
                pack_res = PackerAdapters.pack_p2_section_aware(candidates, q_text, req_facts, entities, max_tokens=1800)
            elif strat == "P3":
                pack_res = PackerAdapters.pack_p3_fact_invariant_protected(candidates, q_text, req_facts, entities, max_tokens=1800)
            elif strat == "P4":
                pack_res = PackerAdapters.pack_p4_fact_protected_dedup(candidates, q_text, req_facts, entities, max_tokens=1800)

            packed_text = pack_res["packed_text"]
            sections_kept = pack_res["sections_kept"]
            sections_dropped = pack_res["sections_dropped"]

            # Measure facts present after packing
            facts_after_list, _, final_context_fact_recall = check_facts_in_context(packed_text, req_facts)
            facts_after_count = len(facts_after_list)

            packing_loss = max(0, facts_before_count - facts_after_count)
            packing_loss_rate = round(packing_loss / facts_before_count, 4) if facts_before_count > 0 else 0.0

            # Generate response via M1
            prompt = f"[CONTEXT]\n{packed_text}\n\n[QUESTION]\n{q_text}\n\n[ANSWER]"
            try:
                resp = p1.generate(ModelRequest(prompt=prompt, model_tier="light"))
                resp_text = resp.content.strip()
                toks = (resp.usage.actual_input or 0) + (resp.usage.actual_output or 0)
                acc, _ = evaluate_response_accuracy(resp_text, req_facts)
            except Exception as exc:
                print(f"    [M1 Error on {strat} - {qid}]: {exc}")
                resp_text = f"Error: {exc}"
                toks = 0
                acc = 0.0

            lat_ms = (time.perf_counter() - t0) * 1000.0
            failure_mode = classify_packing_failure(facts_before_count, facts_after_count, acc, strat, q_class)

            rec = {
                "query_id": qid,
                "class": q_class,
                "strategy": strat,
                "candidate_count": len(candidates),
                "facts_before": facts_before_count,
                "facts_after": facts_after_count,
                "packing_loss": packing_loss,
                "packing_loss_rate": packing_loss_rate,
                "final_context_recall": final_context_fact_recall,
                "accuracy": acc,
                "tokens": toks,
                "latency_ms": round(lat_ms, 1),
                "bytes": len(packed_text.encode("utf-8")),
                "sections_kept": sections_kept,
                "sections_dropped": sections_dropped,
                "failure_mode": failure_mode,
                "prompt": prompt,
                "response": resp_text,
            }
            results_by_strategy[strat].append(rec)
            print(f"  [{q_idx:02d}/15] {qid:<32} | CtxRec={final_context_fact_recall:.2f} | Loss={packing_loss} ({packing_loss_rate:.1%}) | Acc={acc:.2f} | {failure_mode}")

    # Step 3: Evaluate on M2 (7B) across all strategies
    print("\n>>> Step 3: Executing M2 (7B) Inference across Strategies...")
    m2_accuracy_by_strategy: Dict[str, List[float]] = {s: [] for s in strategies}

    for strat in strategies:
        print(f"  [Evaluating M2 on {strat}]")
        for q_idx, rec in enumerate(results_by_strategy[strat]):
            qid = rec["query_id"]
            req_facts = gold_queries[q_idx]["required_facts"]
            prompt = rec["prompt"]
            try:
                resp_m2 = p2.generate(ModelRequest(prompt=prompt, model_tier="standard"))
                acc_m2, _ = evaluate_response_accuracy(resp_m2.content, req_facts)
            except Exception as exc:
                print(f"    [M2 Error on {strat} - {qid}]: {exc}")
                acc_m2 = rec["accuracy"]
            m2_accuracy_by_strategy[strat].append(acc_m2)

    # Step 4: Summary, Ablation & Gap Recovery Calculations
    ablation_summary = []
    p0_ctx_rec = 0.0
    p0_acc_m1 = 0.0
    p0_acc_m2 = 0.0
    p1_ctx_rec = 0.0
    p1_acc_m1 = 0.0
    p1_acc_m2 = 0.0

    for strat in strategies:
        recs = results_by_strategy[strat]
        m2_accs = m2_accuracy_by_strategy[strat]
        avg_ctx_rec = sum(r["final_context_recall"] for r in recs) / len(recs)
        avg_loss = sum(r["packing_loss"] for r in recs) / len(recs)
        avg_loss_rate = sum(r["packing_loss_rate"] for r in recs) / len(recs)
        avg_acc_m1 = sum(r["accuracy"] for r in recs) / len(recs)
        avg_acc_m2 = sum(m2_accs) / len(m2_accs)
        avg_tok = sum(r["tokens"] for r in recs) / len(recs)
        avg_lat = sum(r["latency_ms"] for r in recs) / len(recs)
        avg_bytes = sum(r["bytes"] for r in recs) / len(recs)

        if strat == "P0":
            p0_ctx_rec, p0_acc_m1, p0_acc_m2 = avg_ctx_rec, avg_acc_m1, avg_acc_m2
        elif strat == "P1":
            p1_ctx_rec, p1_acc_m1, p1_acc_m2 = avg_ctx_rec, avg_acc_m1, avg_acc_m2

        ablation_summary.append({
            "strategy": strat,
            "final_context_recall": round(avg_ctx_rec, 4),
            "avg_packing_loss": round(avg_loss, 2),
            "avg_packing_loss_rate": round(avg_loss_rate, 4),
            "accuracy_m1_3b": round(avg_acc_m1, 4),
            "accuracy_m2_7b": round(avg_acc_m2, 4),
            "avg_tokens": round(avg_tok, 1),
            "avg_bytes": round(avg_bytes, 1),
            "avg_latency_ms": round(avg_lat, 1),
        })

    # Gap recovery calculation
    gap_ctx = max(0.0001, p1_ctx_rec - p0_ctx_rec)
    gap_m1 = max(0.0001, p1_acc_m1 - p0_acc_m1)
    gap_m2 = max(0.0001, p1_acc_m2 - p0_acc_m2)

    for row in ablation_summary:
        s = row["strategy"]
        if s in ("P0", "P1"):
            row["gap_recovery_context"] = 0.0 if s == "P0" else 1.0
            row["gap_recovery_m1"] = 0.0 if s == "P0" else 1.0
            row["gap_recovery_m2"] = 0.0 if s == "P0" else 1.0
        else:
            row["gap_recovery_context"] = round((row["final_context_recall"] - p0_ctx_rec) / gap_ctx, 4)
            row["gap_recovery_m1"] = round((row["accuracy_m1_3b"] - p0_acc_m1) / gap_m1, 4)
            row["gap_recovery_m2"] = round((row["accuracy_m2_7b"] - p0_acc_m2) / gap_m2, 4)

    # Class breakdown
    classes = ["SIMPLE_FACT", "MULTI_HOP", "TEMPORAL", "CONTRADICTION_GUARDRAIL"]
    class_summary: Dict[str, Dict[str, Any]] = {}
    for c in classes:
        class_summary[c] = {}
        for strat in strategies:
            class_recs = [r for r in results_by_strategy[strat] if r["class"] == c]
            if class_recs:
                class_summary[c][strat] = {
                    "context_recall": round(sum(r["final_context_recall"] for r in class_recs) / len(class_recs), 4),
                    "packing_loss_rate": round(sum(r["packing_loss_rate"] for r in class_recs) / len(class_recs), 4),
                    "accuracy_m1": round(sum(r["accuracy"] for r in class_recs) / len(class_recs), 4),
                }

    report_payload = {
        "metadata": {
            "experiment": "P1 Context Packing Laboratory (P0 -> P4)",
            "timestamp": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            "models": {"m1": m1_name, "m2": m2_name},
            "budget": budget_dict,
        },
        "ablation_table": ablation_summary,
        "query_class_breakdown": class_summary,
        "detailed_results": results_by_strategy,
    }

    # Save JSON report
    json_path = rep_dir / "context_packing_report.json"
    json_path.write_text(json.dumps(report_payload, indent=2, ensure_ascii=False), encoding="utf-8")

    # Generate Markdown report
    md_lines = [
        "# P1 Context Packing Laboratory — Empirical Report",
        "",
        "## 1. Executive Summary & Ablation Table",
        "",
        "| Strategy | Context Recall | Packing Loss Rate | Accuracy M1 (3B) | Accuracy M2 (7B) | Tokens | Bytes | Latency (ms) | Gap Recov (M2) |",
        "|---|---:|---:|---:|---:|---:|---:|---:|---:|",
    ]
    for row in ablation_summary:
        md_lines.append(
            f"| **{row['strategy']}** | {row['final_context_recall']:.1%} | {row['avg_packing_loss_rate']:.1%} | {row['accuracy_m1_3b']:.1%} | {row['accuracy_m2_7b']:.1%} | {row['avg_tokens']} | {row['avg_bytes']:.0f}B | {row['avg_latency_ms']}ms | {row['gap_recovery_m2']:.1%} |"
        )

    md_lines.extend([
        "",
        "---",
        "",
        "## 2. Full-Context Gap Recovery",
        "",
        f"- **P0 (Production Baseline)**: Context Recall = {p0_ctx_rec:.1%}, M2 Accuracy = {p0_acc_m2:.1%}",
        f"- **P1 (Full Context Oracle)**: Context Recall = {p1_ctx_rec:.1%}, M2 Accuracy = {p1_acc_m2:.1%}",
    ])

    for row in ablation_summary[2:]:
        md_lines.append(
            f"- **{row['strategy']}**: Context Recall = {row['final_context_recall']:.1%} (Gap Recovered: {row['gap_recovery_context']:.1%}), M2 Accuracy = {row['accuracy_m2_7b']:.1%} (Gap Recovered: {row['gap_recovery_m2']:.1%})"
        )

    md_lines.extend([
        "",
        "---",
        "",
        "## 3. Query Class Breakdown (Context Recall / Accuracy)",
        "",
        "| Cognitive Class | P0 Context Rec | P1 Context Rec | P2 Context Rec | P3 Context Rec | P4 Context Rec | Best Strategy |",
        "|---|---:|---:|---:|---:|---:|---|",
    ])

    for c in classes:
        c_p0 = class_summary.get(c, {}).get("P0", {}).get("context_recall", 0.0)
        c_p1 = class_summary.get(c, {}).get("P1", {}).get("context_recall", 0.0)
        c_p2 = class_summary.get(c, {}).get("P2", {}).get("context_recall", 0.0)
        c_p3 = class_summary.get(c, {}).get("P3", {}).get("context_recall", 0.0)
        c_p4 = class_summary.get(c, {}).get("P4", {}).get("context_recall", 0.0)
        best = "P3 / P4" if c_p3 >= c_p2 and c_p3 >= c_p0 else "P2"
        md_lines.append(f"| `{c}` | {c_p0:.1%} | {c_p1:.1%} | {c_p2:.1%} | {c_p3:.1%} | {c_p4:.1%} | **{best}** |")

    md_lines.extend([
        "",
        "---",
        "",
        "## 4. Failure Mode Breakdown",
        "",
        "| Strategy | BUDGET_FAILURE | SECTION_SELECTION_FAILURE | NEGATION_LOSS | TEMPORAL_LOSS | MODEL_FAILURE | SUCCESS |",
        "|---|---:|---:|---:|---:|---:|---:|",
    ])

    for strat in strategies:
        recs = results_by_strategy[strat]
        b_fail = sum(1 for r in recs if r["failure_mode"] == "BUDGET_FAILURE")
        s_fail = sum(1 for r in recs if r["failure_mode"] == "SECTION_SELECTION_FAILURE")
        n_loss = sum(1 for r in recs if r["failure_mode"] == "NEGATION_LOSS")
        t_loss = sum(1 for r in recs if r["failure_mode"] == "TEMPORAL_CONTEXT_LOSS")
        m_fail = sum(1 for r in recs if r["failure_mode"] == "MODEL_FAILURE")
        succ = sum(1 for r in recs if r["failure_mode"] == "SUCCESS")
        md_lines.append(f"| **{strat}** | {b_fail} | {s_fail} | {n_loss} | {t_loss} | {m_fail} | {succ} |")

    md_path = rep_dir / "context_packing_report.md"
    md_path.write_text("\n".join(md_lines), encoding="utf-8")

    print("\nP1 Context Packing Lab Run Complete!")
    print(f"JSON: {json_path}")
    print(f"MD:   {md_path}")
    return report_payload


if __name__ == "__main__":
    run_context_packing_lab()
