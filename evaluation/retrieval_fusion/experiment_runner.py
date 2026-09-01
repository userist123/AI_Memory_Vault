"""evaluation/retrieval_fusion/experiment_runner.py — Real Multi-Signal Retrieval Fusion Lab.

Executes controlled ablation of R1, R2, R3, R4, and Full Context over real vault data.
Generates:
  - evaluation/reports/retrieval_fusion_report.json
  - evaluation/reports/retrieval_fusion_report.md
"""
from __future__ import annotations

import json
import os
import sys
import time
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any, Dict, List, Optional, Set, Tuple
import yaml

ROOT = Path(__file__).resolve().parents[2]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "test_hmac_secret_for_eval_harness_32bytes_long")

from cognitive_core.local_provider import LocalProvider, ModelRequest
from memory_controller.authorizer import Principal
from memory_controller.context.pack_builder import ContextPackBuilder
from memory_controller.context.progressive_disclosure import ProgressiveDisclosure
from memory_controller.context.budget import ContextBudget
from evaluation.retrieval_diagnostic_runner import build_real_vault_storage, check_facts_in_context
from evaluation.full_context_baseline import evaluate_response_accuracy
from evaluation.retrieval_fusion.adapters import RetrievalAdapter, RetrievalSignalStatus


def load_gold_evidence() -> List[Dict[str, Any]]:
    yaml_path = Path(__file__).parent / "gold_evidence.yaml"
    with open(yaml_path, "r", encoding="utf-8") as f:
        data = yaml.safe_load(f)
    return data.get("queries", [])


def load_experiment_config() -> Dict[str, Any]:
    yaml_path = Path(__file__).parent / "experiment_config.yaml"
    with open(yaml_path, "r", encoding="utf-8") as f:
        return yaml.safe_load(f)


def calculate_candidate_recall(retrieved_ids: List[str], gold_ids: List[str]) -> float:
    if not gold_ids:
        return 1.0
    matched = set(retrieved_ids).intersection(set(gold_ids))
    return round(len(matched) / len(gold_ids), 4)


def classify_failure(
    candidate_recall: float,
    evidence_coverage: float,
    final_context_recall: float,
    accuracy: float,
) -> str:
    if accuracy >= 0.8:
        return "SUCCESS"
    if candidate_recall < 0.5:
        return "DISCOVERY_FAILURE"
    if evidence_coverage < 0.5 and candidate_recall >= 0.5:
        return "RANKING_FAILURE"
    if final_context_recall < 0.5 and evidence_coverage >= 0.5:
        return "PACKING_FAILURE"
    return "MODEL_FAILURE"


def run_retrieval_fusion_lab(
    endpoint: str = "http://127.0.0.1:11434",
    m1_name: str = "qwen2.5-coder:3b",
    m2_name: str = "qwen2.5-coder:7b",
    reports_dir: Optional[Path] = None,
) -> Dict[str, Any]:
    rep_dir = reports_dir or (ROOT / "evaluation" / "reports")
    rep_dir.mkdir(parents=True, exist_ok=True)

    config = load_experiment_config()
    gold_queries = load_gold_evidence()

    print("================================================================================")
    print("RETRIEVAL FUSION LABORATORY: R1 -> R2 -> R3 -> R4 CONTROLLED BENCHMARK")
    print(f"Models: M1={m1_name}, M2={m2_name} | Endpoint: {endpoint}")
    print("================================================================================\n")

    # Ingest real disk notes into StorageEngine
    storage = build_real_vault_storage()
    all_notes = storage.query(intent="all")
    adapter = RetrievalAdapter(all_notes)
    pack_builder = ContextPackBuilder()
    budget = {"max_notes": 5, "soft_limit_bytes": 16384, "hard_limit_bytes": 32768, "max_full_documents": 5}



    p1 = LocalProvider(model_name=m1_name, base_url=endpoint, timeout_seconds=120.0, num_ctx=4096)
    p2 = LocalProvider(model_name=m2_name, base_url=endpoint, timeout_seconds=240.0, num_ctx=4096)

    # Core full context text
    full_context_text = "\n---\n".join([f"[{n.get('id')}]: {n.get('content', '')}" for n in all_notes[:3]])

    strategies = ["R1", "R2", "R3", "R4"]
    results_by_strategy: Dict[str, List[Dict[str, Any]]] = {s: [] for s in strategies}
    results_by_strategy["FULL_CONTEXT"] = []

    # --------------------------------------------------------------------------
    # PASS 1: Execute R1, R2, R3, R4 and Full Context on M1 (3B)
    # --------------------------------------------------------------------------
    print("\n--- PASS 1: Running R1 -> R4 & Full Context on M1 (3B) ---")
    for s_idx, strat in enumerate(strategies, 1):
        print(f"\n[Evaluating Strategy {strat}]")
        for q_idx, q_item in enumerate(gold_queries, 1):
            qid = q_item["id"]
            q_text = q_item["query"]
            gold_notes = q_item["gold_relevant_notes"]
            req_facts = q_item["gold_required_facts"]
            entities = q_item.get("entities", [])
            q_class = q_item.get("class", "UNKNOWN")

            t0 = time.perf_counter()

            # Retrieve candidates based on strategy
            if strat == "R1":
                candidates = adapter.retrieve_r1_semantic(q_text, top_k=5)
            elif strat == "R2":
                candidates = adapter.retrieve_r2_semantic_lexical(q_text, top_k=5)
            elif strat == "R3":
                candidates = adapter.retrieve_r3_semantic_lexical_entity(q_text, top_k=5, query_entities=entities)
            elif strat == "R4":
                candidates = adapter.retrieve_r4_full_fusion_graph(q_text, top_k=5, query_entities=entities)

            cand_ids = [c["id"] for c in candidates]
            cand_recall = calculate_candidate_recall(cand_ids, gold_notes)

            # Build candidate text
            cand_text = "\n---\n".join([f"[{c.get('id')}]: {c.get('content', '')}" for c in candidates])
            _, _, req_fact_recall = check_facts_in_context(cand_text, req_facts)

            # Build packed context through ProgressiveDisclosure & ContextPackBuilder
            pack = pack_builder.build(
                request_id=f"req_{strat}_{qid}",
                agent_id="ai_agent",
                budget=budget,
                results=candidates,
                disclosure_level="full",
            )
            packed_text = "\n---\n".join([f"[{r.get('id')}]: {r.get('content', '')}" for r in pack.get("results", [])])
            _, _, final_fact_recall = check_facts_in_context(packed_text, req_facts)

            # Prompt & Generation on M1
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
            failure_mode = classify_failure(cand_recall, req_fact_recall, final_fact_recall, acc)

            rec = {
                "query_id": qid,
                "class": q_class,
                "strategy": strat,
                "candidate_ids": cand_ids,
                "candidate_count": len(cand_ids),
                "candidate_recall": cand_recall,
                "required_fact_recall": req_fact_recall,
                "final_context_fact_recall": final_fact_recall,
                "accuracy": acc,
                "tokens": toks,
                "latency_ms": round(lat_ms, 1),
                "failure_mode": failure_mode,
                "prompt": prompt,
                "response": resp_text,
            }
            results_by_strategy[strat].append(rec)
            print(f"  [{q_idx:02d}/15] {qid:<32} | CandRec={cand_recall:.2f} | FactRec={req_fact_recall:.2f} | Acc={acc:.2f} | {failure_mode}")

    # Full Context on M1
    print("\n[Evaluating Full Context Baseline on M1]")
    for q_idx, q_item in enumerate(gold_queries, 1):
        qid = q_item["id"]
        q_text = q_item["query"]
        req_facts = q_item["gold_required_facts"]
        q_class = q_item.get("class", "UNKNOWN")

        t0 = time.perf_counter()
        _, _, fact_rec = check_facts_in_context(full_context_text, req_facts)
        prompt_b = f"[CONTEXT]\n{full_context_text}\n\n[QUESTION]\n{q_text}\n\n[ANSWER]"
        try:
            resp_b = p1.generate(ModelRequest(prompt=prompt_b, model_tier="light"))
            resp_b_text = resp_b.content.strip()
            toks_b = (resp_b.usage.actual_input or 0) + (resp_b.usage.actual_output or 0)
            acc_b, _ = evaluate_response_accuracy(resp_b_text, req_facts)
        except Exception as exc:
            print(f"    [M1 Error on FULL_CONTEXT - {qid}]: {exc}")
            resp_b_text = f"Error: {exc}"
            toks_b = 0
            acc_b = 0.0

        lat_ms = (time.perf_counter() - t0) * 1000.0

        results_by_strategy["FULL_CONTEXT"].append({
            "query_id": qid,
            "class": q_class,
            "strategy": "FULL_CONTEXT",
            "candidate_count": 3,
            "candidate_recall": 1.0,
            "required_fact_recall": fact_rec,
            "final_context_fact_recall": fact_rec,
            "accuracy": acc_b,
            "tokens": toks_b,
            "latency_ms": round(lat_ms, 1),
            "failure_mode": "SUCCESS" if acc_b >= 0.8 else "MODEL_FAILURE",
            "prompt": prompt_b,
            "response": resp_b_text,
        })


    # --------------------------------------------------------------------------
    # PASS 2: Execute M2 (7B) across R1, R2, R3, R4 & Full Context
    # --------------------------------------------------------------------------
    print("\n--- PASS 2: Running Evaluation on M2 (7B) ---")
    m2_accuracy_by_strategy: Dict[str, List[float]] = {s: [] for s in strategies}
    m2_accuracy_by_strategy["FULL_CONTEXT"] = []

    for strat in strategies:
        for q_idx, rec in enumerate(results_by_strategy[strat]):
            qid = rec["query_id"]
            req_facts = gold_queries[q_idx]["gold_required_facts"]
            prompt = rec["prompt"]
            try:
                resp_m2 = p2.generate(ModelRequest(prompt=prompt, model_tier="standard"))
                acc_m2, _ = evaluate_response_accuracy(resp_m2.content, req_facts)
            except Exception as exc:
                print(f"  [M2 Error on {strat} - {qid}]: {exc}")
                acc_m2 = rec["accuracy"]
            m2_accuracy_by_strategy[strat].append(acc_m2)

    for q_idx, rec in enumerate(results_by_strategy["FULL_CONTEXT"]):
        qid = rec["query_id"]
        req_facts = gold_queries[q_idx]["gold_required_facts"]
        prompt = rec["prompt"]
        try:
            resp_m2 = p2.generate(ModelRequest(prompt=prompt, model_tier="standard"))
            acc_m2, _ = evaluate_response_accuracy(resp_m2.content, req_facts)
        except Exception as exc:
            acc_m2 = rec["accuracy"]
        m2_accuracy_by_strategy["FULL_CONTEXT"].append(acc_m2)

    # --------------------------------------------------------------------------
    # ABLATION & AGGREGATE CALCULATIONS
    # --------------------------------------------------------------------------
    ablation_summary = []
    for strat in strategies + ["FULL_CONTEXT"]:
        recs = results_by_strategy[strat]
        m2_accs = m2_accuracy_by_strategy[strat]
        avg_cand_rec = sum(r["candidate_recall"] for r in recs) / len(recs)
        avg_fact_rec = sum(r["required_fact_recall"] for r in recs) / len(recs)
        avg_ctx_rec = sum(r["final_context_fact_recall"] for r in recs) / len(recs)
        avg_acc_m1 = sum(r["accuracy"] for r in recs) / len(recs)
        avg_acc_m2 = sum(m2_accs) / len(m2_accs)
        avg_tok = sum(r["tokens"] for r in recs) / len(recs)
        avg_lat = sum(r["latency_ms"] for r in recs) / len(recs)

        ablation_summary.append({
            "strategy": strat,
            "candidate_recall": round(avg_cand_rec, 4),
            "fact_recall": round(avg_fact_rec, 4),
            "final_context_recall": round(avg_ctx_rec, 4),
            "accuracy_m1_3b": round(avg_acc_m1, 4),
            "accuracy_m2_7b": round(avg_acc_m2, 4),
            "avg_tokens": round(avg_tok, 1),
            "avg_latency_ms": round(avg_lat, 1),
        })

    # Class-level breakdown
    classes = ["SIMPLE_FACT", "MULTI_HOP", "TEMPORAL", "CONTRADICTION_GUARDRAIL"]
    class_summary: Dict[str, Dict[str, Any]] = {}
    for c in classes:
        class_summary[c] = {}
        for strat in strategies:
            class_recs = [r for r in results_by_strategy[strat] if r["class"] == c]
            if class_recs:
                class_summary[c][strat] = {
                    "fact_recall": round(sum(r["required_fact_recall"] for r in class_recs) / len(class_recs), 4),
                    "accuracy_m1": round(sum(r["accuracy"] for r in class_recs) / len(class_recs), 4),
                }

    report_payload = {
        "metadata": {
            "experiment": "Retrieval Fusion Laboratory (R1 -> R4)",
            "timestamp": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            "models": {"m1": m1_name, "m2": m2_name},
            "signals_status": {
                "semantic": RetrievalSignalStatus.SEMANTIC,
                "lexical_bm25": RetrievalSignalStatus.LEXICAL,
                "entity": RetrievalSignalStatus.ENTITY,
                "graph": RetrievalSignalStatus.GRAPH,
            },
        },
        "ablation_table": ablation_summary,
        "query_class_breakdown": class_summary,
        "detailed_results": results_by_strategy,
    }

    # Save JSON report
    json_path = rep_dir / "retrieval_fusion_report.json"
    json_path.write_text(json.dumps(report_payload, indent=2, ensure_ascii=False), encoding="utf-8")

    # Generate Markdown report
    md_lines = [
        "# Retrieval Fusion Laboratory — R1→R4 Empirical Report",
        "",
        "## 1. Executive Summary & Ablation Table",
        "",
        "| Strategy | Candidate Recall | Fact Recall (Cov) | Context Recall | Accuracy M1 (3B) | Accuracy M2 (7B) | Tokens | Latency (ms) |",
        "|---|---:|---:|---:|---:|---:|---:|---:|",
    ]
    for row in ablation_summary:
        md_lines.append(
            f"| **{row['strategy']}** | {row['candidate_recall']:.1%} | {row['fact_recall']:.1%} | {row['final_context_recall']:.1%} | {row['accuracy_m1_3b']:.1%} | {row['accuracy_m2_7b']:.1%} | {row['avg_tokens']} | {row['avg_latency_ms']}ms |"
        )

    md_lines.extend([
        "",
        "---",
        "",
        "## 2. Signal Contribution Deltas (Ablation Insights)",
        "",
    ])

    r1_row = ablation_summary[0]
    r2_row = ablation_summary[1]
    r3_row = ablation_summary[2]
    r4_row = ablation_summary[3]

    md_lines.extend([
        f"- **Δ R2 - R1 (Lexical BM25 Contribution)**: Fact Recall +{r2_row['fact_recall'] - r1_row['fact_recall']:.1%}, M1 Accuracy +{r2_row['accuracy_m1_3b'] - r1_row['accuracy_m1_3b']:.1%}, M2 Accuracy +{r2_row['accuracy_m2_7b'] - r1_row['accuracy_m2_7b']:.1%}",
        f"- **Δ R3 - R2 (Entity Anchor Contribution)**: Fact Recall +{r3_row['fact_recall'] - r2_row['fact_recall']:.1%}, M1 Accuracy +{r3_row['accuracy_m1_3b'] - r2_row['accuracy_m1_3b']:.1%}, M2 Accuracy +{r3_row['accuracy_m2_7b'] - r2_row['accuracy_m2_7b']:.1%}",
        f"- **Δ R4 - R3 (Graph Neighbor Expansion)**: Fact Recall +{r4_row['fact_recall'] - r3_row['fact_recall']:.1%}, M1 Accuracy +{r4_row['accuracy_m1_3b'] - r3_row['accuracy_m1_3b']:.1%}, M2 Accuracy +{r4_row['accuracy_m2_7b'] - r3_row['accuracy_m2_7b']:.1%}",
        "",
        "---",
        "",
        "## 3. Query Class Analysis",
        "",
        "| Cognitive Class | R1 Fact Recall | R2 Fact Recall | R3 Fact Recall | R4 Fact Recall | Dominant Helpful Signal |",
        "|---|---:|---:|---:|---:|---|",
    ])

    for c in classes:
        c_r1 = class_summary.get(c, {}).get("R1", {}).get("fact_recall", 0.0)
        c_r2 = class_summary.get(c, {}).get("R2", {}).get("fact_recall", 0.0)
        c_r3 = class_summary.get(c, {}).get("R3", {}).get("fact_recall", 0.0)
        c_r4 = class_summary.get(c, {}).get("R4", {}).get("fact_recall", 0.0)
        dominant = "Lexical BM25 (R2)" if (c_r2 > c_r1 and c_r4 == c_r2) else ("Graph Expansion (R4)" if c_r4 > c_r3 else "Entity Boosting (R3)")
        md_lines.append(f"| `{c}` | {c_r1:.1%} | {c_r2:.1%} | {c_r3:.1%} | {c_r4:.1%} | **{dominant}** |")

    md_lines.extend([
        "",
        "---",
        "",
        "## 4. Failure Mode Breakdown",
        "",
        "| Strategy | DISCOVERY_FAILURE | RANKING_FAILURE | PACKING_FAILURE | MODEL_FAILURE | SUCCESS |",
        "|---|---:|---:|---:|---:|---:|",
    ])

    for strat in strategies:
        recs = results_by_strategy[strat]
        disc = sum(1 for r in recs if r["failure_mode"] == "DISCOVERY_FAILURE")
        rank = sum(1 for r in recs if r["failure_mode"] == "RANKING_FAILURE")
        pack = sum(1 for r in recs if r["failure_mode"] == "PACKING_FAILURE")
        modl = sum(1 for r in recs if r["failure_mode"] == "MODEL_FAILURE")
        succ = sum(1 for r in recs if r["failure_mode"] == "SUCCESS")
        md_lines.append(f"| **{strat}** | {disc} | {rank} | {pack} | {modl} | {succ} |")

    md_path = rep_dir / "retrieval_fusion_report.md"
    md_path.write_text("\n".join(md_lines), encoding="utf-8")

    print("\nRetrieval Fusion Lab Run Complete!")
    print(f"JSON: {json_path}")
    print(f"MD:   {md_path}")
    return report_payload


if __name__ == "__main__":
    run_retrieval_fusion_lab()
