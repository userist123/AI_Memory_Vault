"""evaluation/temporal_memory/experiment_runner.py — P2 Temporal Memory Laboratory.

Executes controlled evaluation of temporal conditions:
  - T0: Control Baseline (R4 candidate generation + P2 packing, no temporal traversal)
  - T1: Valid-Time Filtering
  - T2: Supersession Traversal
  - T3: Valid-Time + Supersession Lineage Fusion
  - T4: Bi-Temporal Traversal (Valid Time vs Observation Time)
Generates:
  - evaluation/reports/temporal_memory_report.json
  - evaluation/reports/temporal_memory_report.md
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
from evaluation.temporal_memory.temporal_adapters import TemporalAdapters, audit_temporal_metadata


def load_gold_temporal() -> List[Dict[str, Any]]:
    yaml_path = Path(__file__).parent / "gold_temporal.yaml"
    with open(yaml_path, "r", encoding="utf-8") as f:
        data = yaml.safe_load(f)
    return data.get("queries", [])


def load_experiment_config() -> Dict[str, Any]:
    yaml_path = Path(__file__).parent / "experiment_config.yaml"
    with open(yaml_path, "r", encoding="utf-8") as f:
        return yaml.safe_load(f)


def classify_temporal_failure(
    facts_before: int,
    facts_after: int,
    accuracy: float,
    condition: str,
    q_class: str,
) -> str:
    if accuracy >= 0.75:
        return "SUCCESS"
    if facts_before == 0:
        return "SUPERSESSION_DISCOVERY_FAILURE"
    if facts_after == 0:
        return "TEMPORAL_FILTER_FAILURE"
    if facts_after < facts_before:
        return "PACKING_FAILURE"
    return "MODEL_FAILURE"


def run_temporal_memory_lab(
    endpoint: str = "http://127.0.0.1:11434",
    m1_name: str = "qwen2.5-coder:3b",
    m2_name: str = "qwen2.5-coder:7b",
    reports_dir: Optional[Path] = None,
) -> Dict[str, Any]:
    rep_dir = reports_dir or (ROOT / "07_EVALUATION" / "reports")
    rep_dir.mkdir(parents=True, exist_ok=True)

    config = load_experiment_config()
    gold_queries = load_gold_temporal()

    print("================================================================================")
    print("P2 TEMPORAL MEMORY LABORATORY: T0 -> T1 -> T2 -> T3 -> T4 BENCHMARK")
    print(f"Models: M1={m1_name}, M2={m2_name} | Endpoint: {endpoint}")
    print("================================================================================\n")

    # Ingest real disk notes into StorageEngine
    storage = build_real_vault_storage()
    all_notes = storage.query(intent="all")
    all_notes_by_id = {str(n.get("id")): n for n in all_notes}
    retrieval_adapter = RetrievalAdapter(all_notes)

    # Step 1: Audit temporal metadata across the Vault
    temporal_audit = audit_temporal_metadata(all_notes)
    print(">>> Step 1: Temporal Metadata Audit across Vault Notes:")
    for field_name, f_info in temporal_audit["fields"].items():
        print(f"  - {field_name:<18}: {f_info['count']:>4} notes ({f_info['pct']:>5.1f}%) | {f_info['status']}")

    p1 = LocalProvider(model_name=m1_name, base_url=endpoint, timeout_seconds=120.0, num_ctx=4096)
    p2 = LocalProvider(model_name=m2_name, base_url=endpoint, timeout_seconds=240.0, num_ctx=4096)

    conditions = ["T0", "T1", "T2", "T3", "T4"]
    results_by_condition: Dict[str, List[Dict[str, Any]]] = {c: [] for c in conditions}

    # Step 2: Pre-retrieve R4 candidates for all temporal queries
    print("\n>>> Step 2: Generating Base R4 Candidates for Temporal Queries...")
    base_candidates: Dict[str, List[Dict[str, Any]]] = {}
    for q_item in gold_queries:
        qid = q_item["id"]
        q_text = q_item["query"]
        entities = q_item.get("entities", [])
        cands = retrieval_adapter.retrieve_r4_full_fusion_graph(q_text, top_k=5, query_entities=entities)
        base_candidates[qid] = cands

    # Step 3: Run conditions on M1 (3B)
    print("\n>>> Step 3: Executing Temporal Traversal Conditions on M1 (3B)...")
    for cond in conditions:
        print(f"\n[Evaluating Temporal Condition: {cond}]")
        for q_idx, q_item in enumerate(gold_queries, 1):
            qid = q_item["id"]
            q_text = q_item["query"]
            req_facts = q_item.get("required_evidence", [])
            entities = q_item.get("entities", [])
            q_class = q_item.get("class", "UNKNOWN")
            candidates = base_candidates[qid]

            t0 = time.perf_counter()

            # Apply temporal condition
            if cond == "T0":
                temporal_cands = list(candidates)
            elif cond == "T1":
                temporal_cands = TemporalAdapters.apply_t1_valid_time_filter(candidates, q_text)
            elif cond == "T2":
                temporal_cands = TemporalAdapters.apply_t2_supersession_traversal(candidates, all_notes_by_id)
            elif cond == "T3":
                temporal_cands = TemporalAdapters.apply_t3_valid_time_and_supersession(candidates, all_notes_by_id, q_text)
            elif cond == "T4":
                temporal_cands = TemporalAdapters.apply_t4_bitemporal_traversal(candidates, all_notes_by_id, q_text)

            # Check facts before packing
            cand_raw_text = "\n---\n".join([f"[{c.get('id')}]: {c.get('content', '')}" for c in temporal_cands])
            facts_before_list, _, _ = check_facts_in_context(cand_raw_text, req_facts)
            facts_before_count = len(facts_before_list)

            # Pack using standard P2 section-aware extraction
            pack_res = PackerAdapters.pack_p2_section_aware(temporal_cands, q_text, req_facts, entities, max_tokens=1800)
            packed_text = pack_res["packed_text"]

            # Check facts after packing
            facts_after_list, _, final_context_fact_recall = check_facts_in_context(packed_text, req_facts)
            facts_after_count = len(facts_after_list)

            # Prompt execution
            prompt = (
                f"[TEMPORAL MEMORY CONTEXT]\n{packed_text}\n\n"
                f"[QUESTION]\n{q_text}\n\n"
                f"[INSTRUCTION]\nAnswer precisely based strictly on the temporal validity and lineage above. If temporal validity is missing or unknown, state UNKNOWN explicitly.\n\n"
                f"[ANSWER]"
            )

            try:
                resp = p1.generate(ModelRequest(prompt=prompt, model_tier="light"))
                resp_text = resp.content.strip()
                toks = (resp.usage.actual_input or 0) + (resp.usage.actual_output or 0)
                acc, _ = evaluate_response_accuracy(resp_text, req_facts)
            except Exception as exc:
                print(f"    [M1 Error on {cond} - {qid}]: {exc}")
                resp_text = f"Error: {exc}"
                toks = 0
                acc = 0.0

            lat_ms = (time.perf_counter() - t0) * 1000.0
            failure_mode = classify_temporal_failure(facts_before_count, facts_after_count, acc, cond, q_class)

            rec = {
                "query_id": qid,
                "class": q_class,
                "condition": cond,
                "candidate_count": len(temporal_cands),
                "facts_before": facts_before_count,
                "facts_after": facts_after_count,
                "final_context_recall": final_context_fact_recall,
                "accuracy": acc,
                "tokens": toks,
                "latency_ms": round(lat_ms, 1),
                "failure_mode": failure_mode,
                "prompt": prompt,
                "response": resp_text,
            }
            results_by_condition[cond].append(rec)
            print(f"  [{q_idx:02d}/07] {qid:<32} | CtxRec={final_context_fact_recall:.2f} | Acc={acc:.2f} | {failure_mode}")

    # Step 4: Run conditions on M2 (7B)
    print("\n>>> Step 4: Executing Temporal Traversal Conditions on M2 (7B)...")
    m2_accuracy_by_condition: Dict[str, List[float]] = {c: [] for c in conditions}

    for cond in conditions:
        print(f"  [Evaluating M2 on {cond}]")
        for q_idx, rec in enumerate(results_by_condition[cond]):
            qid = rec["query_id"]
            req_facts = gold_queries[q_idx].get("required_evidence", [])
            prompt = rec["prompt"]
            try:
                resp_m2 = p2.generate(ModelRequest(prompt=prompt, model_tier="standard"))
                acc_m2, _ = evaluate_response_accuracy(resp_m2.content, req_facts)
            except Exception as exc:
                print(f"    [M2 Error on {cond} - {qid}]: {exc}")
                acc_m2 = rec["accuracy"]
            m2_accuracy_by_condition[cond].append(acc_m2)

    # Step 5: Summarize Results & Metrics
    summary_table = []
    for cond in conditions:
        recs = results_by_condition[cond]
        m2_accs = m2_accuracy_by_condition[cond]
        avg_ctx_rec = sum(r["final_context_recall"] for r in recs) / len(recs)
        avg_acc_m1 = sum(r["accuracy"] for r in recs) / len(recs)
        avg_acc_m2 = sum(m2_accs) / len(m2_accs)
        avg_tok = sum(r["tokens"] for r in recs) / len(recs)
        avg_lat = sum(r["latency_ms"] for r in recs) / len(recs)

        summary_table.append({
            "condition": cond,
            "final_context_recall": round(avg_ctx_rec, 4),
            "accuracy_m1_3b": round(avg_acc_m1, 4),
            "accuracy_m2_7b": round(avg_acc_m2, 4),
            "avg_tokens": round(avg_tok, 1),
            "avg_latency_ms": round(avg_lat, 1),
        })

    # Class breakdown
    classes = list({q["class"] for q in gold_queries})
    class_summary: Dict[str, Dict[str, Any]] = {}
    for c in classes:
        class_summary[c] = {}
        for cond in conditions:
            c_recs = [r for r in results_by_condition[cond] if r["class"] == c]
            if c_recs:
                class_summary[c][cond] = {
                    "context_recall": round(sum(r["final_context_recall"] for r in c_recs) / len(c_recs), 4),
                    "accuracy_m1": round(sum(r["accuracy"] for r in c_recs) / len(c_recs), 4),
                }

    report_payload = {
        "metadata": {
            "experiment": "P2 Temporal Memory Laboratory (T0 -> T4)",
            "timestamp": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            "models": {"m1": m1_name, "m2": m2_name},
            "temporal_audit": temporal_audit,
        },
        "summary_table": summary_table,
        "class_breakdown": class_summary,
        "detailed_results": results_by_condition,
    }

    # Save JSON report
    json_path = rep_dir / "temporal_memory_report.json"
    json_path.write_text(json.dumps(report_payload, indent=2, ensure_ascii=False), encoding="utf-8")

    # Save Markdown report
    md_lines = [
        "# P2 Temporal Memory Laboratory — Empirical Report",
        "",
        "## 1. Executive Summary & Temporal Ablation Table",
        "",
        "| Condition | Context Recall | Accuracy M1 (3B) | Accuracy M2 (7B) | Tokens | Latency (ms) |",
        "|---|---:|---:|---:|---:|---:|",
    ]
    for row in summary_table:
        md_lines.append(
            f"| **{row['condition']}** | {row['final_context_recall']:.1%} | {row['accuracy_m1_3b']:.1%} | {row['accuracy_m2_7b']:.1%} | {row['avg_tokens']} | {row['avg_latency_ms']}ms |"
        )

    md_lines.extend([
        "",
        "---",
        "",
        "## 2. Temporal Metadata Audit (Vault Notes)",
        "",
        "| Metadata Field | Present Notes | Percentage | Availability Status |",
        "|---|---:|---:|:---:|",
    ])
    for f_name, f_info in temporal_audit["fields"].items():
        md_lines.append(f"| `{f_name}` | {f_info['count']} | {f_info['pct']}% | **{f_info['status']}** |")

    md_lines.extend([
        "",
        "---",
        "",
        "## 3. Query Class Breakdown (Context Recall / Accuracy)",
        "",
        "| Cognitive Class | T0 Context Rec | T1 Context Rec | T2 Context Rec | T3 Context Rec | T4 Context Rec | Best Condition |",
        "|---|---:|---:|---:|---:|---:|---|",
    ])
    for c in sorted(classes):
        c_t0 = class_summary.get(c, {}).get("T0", {}).get("context_recall", 0.0)
        c_t1 = class_summary.get(c, {}).get("T1", {}).get("context_recall", 0.0)
        c_t2 = class_summary.get(c, {}).get("T2", {}).get("context_recall", 0.0)
        c_t3 = class_summary.get(c, {}).get("T3", {}).get("context_recall", 0.0)
        c_t4 = class_summary.get(c, {}).get("T4", {}).get("context_recall", 0.0)
        best = "T4" if c_t4 >= c_t0 and c_t4 >= c_t1 else ("T2" if c_t2 >= c_t0 else "T0")
        md_lines.append(f"| `{c}` | {c_t0:.1%} | {c_t1:.1%} | {c_t2:.1%} | {c_t3:.1%} | {c_t4:.1%} | **{best}** |")

    md_lines.extend([
        "",
        "---",
        "",
        "## 4. Failure Mode Breakdown",
        "",
        "| Condition | SUPERSESSION_DISCOVERY | TEMPORAL_FILTER_FAIL | PACKING_FAIL | MODEL_FAIL | SUCCESS |",
        "|---|---:|---:|---:|---:|---:|",
    ])
    for cond in conditions:
        recs = results_by_condition[cond]
        s_fail = sum(1 for r in recs if r["failure_mode"] == "SUPERSESSION_DISCOVERY_FAILURE")
        t_fail = sum(1 for r in recs if r["failure_mode"] == "TEMPORAL_FILTER_FAILURE")
        p_fail = sum(1 for r in recs if r["failure_mode"] == "PACKING_FAILURE")
        m_fail = sum(1 for r in recs if r["failure_mode"] == "MODEL_FAILURE")
        succ = sum(1 for r in recs if r["failure_mode"] == "SUCCESS")
        md_lines.append(f"| **{cond}** | {s_fail} | {t_fail} | {p_fail} | {m_fail} | {succ} |")

    md_path = rep_dir / "temporal_memory_report.md"
    md_path.write_text("\n".join(md_lines), encoding="utf-8")

    print("\nP2 Temporal Memory Lab Run Complete!")
    print(f"JSON: {json_path}")
    print(f"MD:   {md_path}")
    return report_payload


if __name__ == "__main__":
    run_temporal_memory_lab()
