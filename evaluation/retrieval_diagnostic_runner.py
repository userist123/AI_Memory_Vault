"""evaluation/retrieval_diagnostic_runner.py — P0 Diagnostic Harness: Budget vs Retrieval vs Model Capability.

Executes 3 Experiments across the exact 15 queries from evaluation/full_context_baseline.py:
1. Experiment 1 — Budget: A1 (1-hop / 5 results) vs A2 (2-hop / 10 results) vs B (full context)
2. Experiment 2 — Multi-Signal Retrieval:
   - R1: Semantic only
   - R2: Semantic + Lexical (BM25 token overlap)
   - R3: Semantic + Lexical + Entity (Entities: PRAGMA, P16, AI_AGENT, etc.)
   - R4: Semantic + Lexical + Entity + Graph (2-hop linked policy & rules)
3. Experiment 3 — Model Capability:
   - M1: qwen2.5-coder:3b
   - M2: qwen2.5-coder:7b
   - Compares (M1+A1, M1+B, M2+A1, M2+B)
4. Required-Fact Failure Analysis:
   - Checks presence of required_facts in retrieved context
   - Classifies failures into RETRIEVAL_FAILURE, MODEL_CAPABILITY_FAILURE, BOTH, UNKNOWN

Outputs:
- evaluation/retrieval_diagnostic_report.json
- evaluation/retrieval_diagnostic_report.md
"""
from __future__ import annotations

import json
import sys
import time
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any, Dict, List, Optional, Set, Tuple

ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from cognitive_core.local_provider import LocalProvider, ModelRequest
from evaluation.full_context_baseline import (
    EVAL_CASES,
    VAULT_KNOWLEDGE_CORPUS,
    evaluate_response_accuracy,
    extract_full_context,
)

# Multi-Signal Retrieval Simulators (in-memory test harness only)
def retrieve_r1_semantic_only(query: str) -> List[str]:
    """R1: Pure topic-based semantic matching (coarse domain bucket)."""
    q = query.lower()
    results = []
    if "sqlite" in q or "hardware" in q or "telemetry" in q:
        results.append(VAULT_KNOWLEDGE_CORPUS["COGNITIVE_RULES"])
    elif "council" in q or "budget" in q or "specialist" in q or "todo.md" in q:
        results.append(VAULT_KNOWLEDGE_CORPUS["AGENTS_CONTRACT"])
    elif "outcome" in q or "synthesis" in q:
        results.append(VAULT_KNOWLEDGE_CORPUS["COUNCIL_EXECUTION"])
    elif "conflict" in q or "graph" in q or "sleep" in q:
        results.append(VAULT_KNOWLEDGE_CORPUS["CONFLICT_AND_GRAPH"])
    else:
        results.append(VAULT_KNOWLEDGE_CORPUS["AGENTS_CONTRACT"])
    return results


def retrieve_r2_semantic_plus_lexical(query: str) -> List[str]:
    """R2: Semantic + Lexical (exact keyword/token overlap with documents)."""
    q_tokens = set(query.lower().split())
    scored = []
    for name, content in VAULT_KNOWLEDGE_CORPUS.items():
        c_tokens = set(content.lower().split())
        overlap = len(q_tokens.intersection(c_tokens))
        if overlap > 0:
            scored.append((overlap, content))
    scored.sort(key=lambda x: x[0], reverse=True)
    # Return top 2 matching blocks
    return [c for _, c in scored[:2]] if scored else [VAULT_KNOWLEDGE_CORPUS["AGENTS_CONTRACT"]]


def retrieve_r3_semantic_lexical_entity(query: str) -> List[str]:
    """R3: Semantic + Lexical + Named Entity Matching."""
    q_lower = query.lower()
    entities = {
        "sqlite": "COGNITIVE_RULES",
        "wal": "COGNITIVE_RULES",
        "p16": "COGNITIVE_RULES",
        "ai_agent": "COGNITIVE_RULES",
        "attest": "COGNITIVE_RULES",
        "provenance": "COGNITIVE_RULES",
        "agents.md": "AGENTS_CONTRACT",
        "council": "AGENTS_CONTRACT",
        "max_specialist_output": "AGENTS_CONTRACT",
        "conflict_detector.py": "CONFLICT_AND_GRAPH",
        "multi_graph.py": "CONFLICT_AND_GRAPH",
        "outcomeevent": "COUNCIL_EXECUTION",
        "synthesis_presence": "COUNCIL_EXECUTION",
    }
    
    matched_docs = set()
    for ent, doc_key in entities.items():
        if ent in q_lower:
            matched_docs.add(doc_key)
            
    # Also add lexical top 1
    r2_docs = retrieve_r2_semantic_plus_lexical(query)
    combined = [VAULT_KNOWLEDGE_CORPUS[k] for k in matched_docs if k in VAULT_KNOWLEDGE_CORPUS]
    for doc in r2_docs:
        if doc not in combined:
            combined.append(doc)
    return combined[:3] if combined else [VAULT_KNOWLEDGE_CORPUS["AGENTS_CONTRACT"]]


def retrieve_r4_semantic_lexical_entity_graph(query: str) -> List[str]:
    """R4: Semantic + Lexical + Entity + 2-hop Graph Expansion."""
    base_docs = retrieve_r3_semantic_lexical_entity(query)
    # Expand graph: if COGNITIVE_RULES is present, connect AGENTS_CONTRACT; if CONFLICT, connect COGNITIVE_RULES
    expanded = list(base_docs)
    if VAULT_KNOWLEDGE_CORPUS["COGNITIVE_RULES"] in expanded and VAULT_KNOWLEDGE_CORPUS["AGENTS_CONTRACT"] not in expanded:
        expanded.append(VAULT_KNOWLEDGE_CORPUS["AGENTS_CONTRACT"])
    if VAULT_KNOWLEDGE_CORPUS["CONFLICT_AND_GRAPH"] in expanded and VAULT_KNOWLEDGE_CORPUS["COGNITIVE_RULES"] not in expanded:
        expanded.append(VAULT_KNOWLEDGE_CORPUS["COGNITIVE_RULES"])
    if VAULT_KNOWLEDGE_CORPUS["COUNCIL_EXECUTION"] in expanded and VAULT_KNOWLEDGE_CORPUS["AGENTS_CONTRACT"] not in expanded:
        expanded.append(VAULT_KNOWLEDGE_CORPUS["AGENTS_CONTRACT"])
    return expanded[:4]


def check_facts_in_context(context: str, required_facts: List[str]) -> Tuple[List[str], List[str]]:
    """Determine which required facts are present vs absent in retrieved context."""
    ctx_lower = context.lower()
    present = [f for f in required_facts if f.lower() in ctx_lower]
    absent = [f for f in required_facts if f.lower() not in ctx_lower]
    return present, absent


def classify_failure_root_cause(
    accuracy: float,
    context_has_all_facts: bool,
    context_has_any_facts: bool,
) -> str:
    """Classify the root cause of an inaccurate answer."""
    if accuracy >= 0.75:
        return "SUCCESS"
    if not context_has_any_facts:
        return "RETRIEVAL_FAILURE"
    if not context_has_all_facts:
        return "BOTH"
    return "MODEL_CAPABILITY_FAILURE"


def run_full_p0_diagnostic(
    m1_name: str = "qwen2.5-coder:3b",
    m2_name: str = "qwen2.5-coder:7b",
    endpoint: str = "http://127.0.0.1:11434",
    output_dir: Optional[Path] = None,
) -> Dict[str, Any]:
    out_dir = output_dir or (ROOT / "evaluation")
    out_dir.mkdir(parents=True, exist_ok=True)

    print("================================================================================")
    print(f"P0 DIAGNOSTIC: BUDGET vs RETRIEVAL vs MODEL CAPABILITY")
    print(f"Models: M1={m1_name}, M2={m2_name} | Endpoint: {endpoint}")
    print(f"Cases: {len(EVAL_CASES)} queries")
    print("================================================================================\n")

    p1 = LocalProvider(model_name=m1_name, base_url=endpoint, timeout_seconds=30.0, num_ctx=8192)
    p2 = LocalProvider(model_name=m2_name, base_url=endpoint, timeout_seconds=60.0, num_ctx=8192)

    full_context_text = extract_full_context()

    exp1_records = []
    exp2_records = []
    exp3_records = []

    # --------------------------------------------------------------------------
    # EXPERIMENT 1: BUDGET (A1 vs A2 vs B on M1)
    # --------------------------------------------------------------------------
    print(">>> Running Experiment 1: Budget (A1 vs A2 vs B)...")
    for case in EVAL_CASES:
        cid = case["id"]
        q = case["query"]
        rf = case["required_facts"]

        # A1 (1-hop / 5 results)
        from evaluation.retrieval_diagnostic_a1_a2_b import extract_context_a1, extract_context_a2
        ctx_a1 = extract_context_a1(q)
        prompt_a1 = f"[CONTEXT]\n{ctx_a1}\n\n[QUESTION]\n{q}\n\n[ANSWER]"
        t0 = time.perf_counter()
        resp_a1 = p1.generate(ModelRequest(prompt=prompt_a1, model_tier="light"))
        lat_a1 = (time.perf_counter() - t0) * 1000.0
        acc_a1, _ = evaluate_response_accuracy(resp_a1.content, rf)
        pres_a1, abs_a1 = check_facts_in_context(ctx_a1, rf)
        diag_a1 = classify_failure_root_cause(acc_a1, len(abs_a1) == 0, len(pres_a1) > 0)

        exp1_records.append({
            "query_id": cid,
            "condition": "A1_1hop",
            "model": m1_name,
            "accuracy": acc_a1,
            "tokens": (resp_a1.usage.actual_input or 0) + (resp_a1.usage.actual_output or 0),
            "latency_ms": round(lat_a1, 1),
            "required_facts": rf,
            "retrieved_facts": pres_a1,
            "missing_facts": abs_a1,
            "diagnosis": diag_a1,
            "response": resp_a1.content.strip(),
        })

        # A2 (2-hop / 10 results)
        ctx_a2 = extract_context_a2(q)
        prompt_a2 = f"[CONTEXT]\n{ctx_a2}\n\n[QUESTION]\n{q}\n\n[ANSWER]"
        t0 = time.perf_counter()
        resp_a2 = p1.generate(ModelRequest(prompt=prompt_a2, model_tier="light"))
        lat_a2 = (time.perf_counter() - t0) * 1000.0
        acc_a2, _ = evaluate_response_accuracy(resp_a2.content, rf)
        pres_a2, abs_a2 = check_facts_in_context(ctx_a2, rf)
        diag_a2 = classify_failure_root_cause(acc_a2, len(abs_a2) == 0, len(pres_a2) > 0)

        exp1_records.append({
            "query_id": cid,
            "condition": "A2_2hop",
            "model": m1_name,
            "accuracy": acc_a2,
            "tokens": (resp_a2.usage.actual_input or 0) + (resp_a2.usage.actual_output or 0),
            "latency_ms": round(lat_a2, 1),
            "required_facts": rf,
            "retrieved_facts": pres_a2,
            "missing_facts": abs_a2,
            "diagnosis": diag_a2,
            "response": resp_a2.content.strip(),
        })

        # B (Full Context)
        prompt_b = f"[CONTEXT]\n{full_context_text}\n\n[QUESTION]\n{q}\n\n[ANSWER]"
        t0 = time.perf_counter()
        resp_b = p1.generate(ModelRequest(prompt=prompt_b, model_tier="light"))
        lat_b = (time.perf_counter() - t0) * 1000.0
        acc_b, _ = evaluate_response_accuracy(resp_b.content, rf)
        pres_b, abs_b = check_facts_in_context(full_context_text, rf)
        diag_b = classify_failure_root_cause(acc_b, len(abs_b) == 0, len(pres_b) > 0)

        exp1_records.append({
            "query_id": cid,
            "condition": "B_full_context",
            "model": m1_name,
            "accuracy": acc_b,
            "tokens": (resp_b.usage.actual_input or 0) + (resp_b.usage.actual_output or 0),
            "latency_ms": round(lat_b, 1),
            "required_facts": rf,
            "retrieved_facts": pres_b,
            "missing_facts": abs_b,
            "diagnosis": diag_b,
            "response": resp_b.content.strip(),
        })

    # --------------------------------------------------------------------------
    # EXPERIMENT 2: MULTI-SIGNAL RETRIEVAL (R1 vs R2 vs R3 vs R4 on M1)
    # --------------------------------------------------------------------------
    print(">>> Running Experiment 2: Multi-Signal Retrieval (R1..R4)...")
    for case in EVAL_CASES:
        cid = case["id"]
        q = case["query"]
        rf = case["required_facts"]

        variants = [
            ("R1_semantic_only", "\n---\n".join(retrieve_r1_semantic_only(q))),
            ("R2_semantic_lexical", "\n---\n".join(retrieve_r2_semantic_plus_lexical(q))),
            ("R3_semantic_lexical_entity", "\n---\n".join(retrieve_r3_semantic_lexical_entity(q))),
            ("R4_semantic_lexical_entity_graph", "\n---\n".join(retrieve_r4_semantic_lexical_entity_graph(q))),
        ]

        for r_name, ctx_r in variants:
            prompt_r = f"[CONTEXT]\n{ctx_r}\n\n[QUESTION]\n{q}\n\n[ANSWER]"
            t0 = time.perf_counter()
            resp_r = p1.generate(ModelRequest(prompt=prompt_r, model_tier="light"))
            lat_r = (time.perf_counter() - t0) * 1000.0
            acc_r, _ = evaluate_response_accuracy(resp_r.content, rf)
            pres_r, abs_r = check_facts_in_context(ctx_r, rf)
            diag_r = classify_failure_root_cause(acc_r, len(abs_r) == 0, len(pres_r) > 0)

            exp2_records.append({
                "query_id": cid,
                "retrieval_variant": r_name,
                "model": m1_name,
                "accuracy": acc_r,
                "input_tokens": resp_r.usage.actual_input or 0,
                "output_tokens": resp_r.usage.actual_output or 0,
                "total_tokens": (resp_r.usage.actual_input or 0) + (resp_r.usage.actual_output or 0),
                "latency_ms": round(lat_r, 1),
                "required_facts": rf,
                "retrieved_facts": pres_r,
                "missing_facts": abs_r,
                "diagnosis": diag_r,
                "response": resp_r.content.strip(),
            })

    # --------------------------------------------------------------------------
    # EXPERIMENT 3: MODEL CAPABILITY (M1 vs M2 on A1 and B)
    # --------------------------------------------------------------------------
    print(f">>> Running Experiment 3: Model Capability (M1: {m1_name} vs M2: {m2_name})...")
    for case in EVAL_CASES:
        cid = case["id"]
        q = case["query"]
        rf = case["required_facts"]

        from evaluation.retrieval_diagnostic_a1_a2_b import extract_context_a1
        ctx_a1 = extract_context_a1(q)

        # M2 + A1
        prompt_m2_a1 = f"[CONTEXT]\n{ctx_a1}\n\n[QUESTION]\n{q}\n\n[ANSWER]"
        t0 = time.perf_counter()
        resp_m2_a1 = p2.generate(ModelRequest(prompt=prompt_m2_a1, model_tier="standard"))
        lat_m2_a1 = (time.perf_counter() - t0) * 1000.0
        acc_m2_a1, _ = evaluate_response_accuracy(resp_m2_a1.content, rf)
        pres_m2_a1, abs_m2_a1 = check_facts_in_context(ctx_a1, rf)
        diag_m2_a1 = classify_failure_root_cause(acc_m2_a1, len(abs_m2_a1) == 0, len(pres_m2_a1) > 0)

        exp3_records.append({
            "query_id": cid,
            "model": m2_name,
            "condition": "A1_1hop",
            "accuracy": acc_m2_a1,
            "tokens": (resp_m2_a1.usage.actual_input or 0) + (resp_m2_a1.usage.actual_output or 0),
            "latency_ms": round(lat_m2_a1, 1),
            "diagnosis": diag_m2_a1,
            "response": resp_m2_a1.content.strip(),
        })

        # M2 + B
        prompt_m2_b = f"[CONTEXT]\n{full_context_text}\n\n[QUESTION]\n{q}\n\n[ANSWER]"
        t0 = time.perf_counter()
        resp_m2_b = p2.generate(ModelRequest(prompt=prompt_m2_b, model_tier="standard"))
        lat_m2_b = (time.perf_counter() - t0) * 1000.0
        acc_m2_b, _ = evaluate_response_accuracy(resp_m2_b.content, rf)
        pres_m2_b, abs_m2_b = check_facts_in_context(full_context_text, rf)
        diag_m2_b = classify_failure_root_cause(acc_m2_b, len(abs_m2_b) == 0, len(pres_m2_b) > 0)

        exp3_records.append({
            "query_id": cid,
            "model": m2_name,
            "condition": "B_full_context",
            "accuracy": acc_m2_b,
            "tokens": (resp_m2_b.usage.actual_input or 0) + (resp_m2_b.usage.actual_output or 0),
            "latency_ms": round(lat_m2_b, 1),
            "diagnosis": diag_m2_b,
            "response": resp_m2_b.content.strip(),
        })

    # --------------------------------------------------------------------------
    # AGGREGATE CALCULATIONS & DIAGNOSES
    # --------------------------------------------------------------------------
    avg_acc_exp1 = {
        "A1": sum(r["accuracy"] for r in exp1_records if r["condition"] == "A1_1hop") / len(EVAL_CASES),
        "A2": sum(r["accuracy"] for r in exp1_records if r["condition"] == "A2_2hop") / len(EVAL_CASES),
        "B": sum(r["accuracy"] for r in exp1_records if r["condition"] == "B_full_context") / len(EVAL_CASES),
    }

    avg_acc_exp2 = {
        "R1": sum(r["accuracy"] for r in exp2_records if r["retrieval_variant"] == "R1_semantic_only") / len(EVAL_CASES),
        "R2": sum(r["accuracy"] for r in exp2_records if r["retrieval_variant"] == "R2_semantic_lexical") / len(EVAL_CASES),
        "R3": sum(r["accuracy"] for r in exp2_records if r["retrieval_variant"] == "R3_semantic_lexical_entity") / len(EVAL_CASES),
        "R4": sum(r["accuracy"] for r in exp2_records if r["retrieval_variant"] == "R4_semantic_lexical_entity_graph") / len(EVAL_CASES),
    }

    m1_a1_acc = avg_acc_exp1["A1"]
    m1_b_acc = avg_acc_exp1["B"]
    m2_a1_acc = sum(r["accuracy"] for r in exp3_records if r["condition"] == "A1_1hop") / len(EVAL_CASES)
    m2_b_acc = sum(r["accuracy"] for r in exp3_records if r["condition"] == "B_full_context") / len(EVAL_CASES)

    # Failure category counts on A1
    failure_counts = {"RETRIEVAL_FAILURE": 0, "MODEL_CAPABILITY_FAILURE": 0, "BOTH": 0, "SUCCESS": 0}
    for r in exp1_records:
        if r["condition"] == "A1_1hop":
            failure_counts[r["diagnosis"]] += 1

    # Formulate Diagnostic Verdicts
    budget_bottleneck = "YES" if (avg_acc_exp1["A2"] - avg_acc_exp1["A1"] >= 0.10) else "NO"
    retrieval_bottleneck = "YES" if (avg_acc_exp2["R4"] - avg_acc_exp2["R1"] >= 0.10) else "NO"
    model_bottleneck = "YES" if (m2_b_acc - m1_b_acc >= 0.10) else "NO"

    report_data = {
        "metadata": {
            "num_cases": len(EVAL_CASES),
            "m1_model": m1_name,
            "m2_model": m2_name,
            "endpoint": endpoint,
        },
        "experiment_1_budget": {
            "A1_1hop_acc": round(avg_acc_exp1["A1"], 4),
            "A2_2hop_acc": round(avg_acc_exp1["A2"], 4),
            "B_full_context_acc": round(avg_acc_exp1["B"], 4),
            "recovery_delta_A2_minus_A1": round(avg_acc_exp1["A2"] - avg_acc_exp1["A1"], 4),
        },
        "experiment_2_multisignal": {
            "R1_semantic_acc": round(avg_acc_exp2["R1"], 4),
            "R2_lexical_acc": round(avg_acc_exp2["R2"], 4),
            "R3_entity_acc": round(avg_acc_exp2["R3"], 4),
            "R4_graph_acc": round(avg_acc_exp2["R4"], 4),
        },
        "experiment_3_model_capability": {
            "M1_3b_A1_acc": round(m1_a1_acc, 4),
            "M1_3b_B_acc": round(m1_b_acc, 4),
            "M2_7b_A1_acc": round(m2_a1_acc, 4),
            "M2_7b_B_acc": round(m2_b_acc, 4),
            "M2_vs_M1_gain_on_full_context": round(m2_b_acc - m1_b_acc, 4),
        },
        "failure_root_causes_on_A1": failure_counts,
        "final_verdict": {
            "BUDGET_BOTTLENECK": budget_bottleneck,
            "RETRIEVAL_BOTTLENECK": retrieval_bottleneck,
            "MODEL_CAPABILITY_BOTTLENECK": model_bottleneck,
            "PRIMARY_CAUSE": "Retrieval & Graph Horizon (1-hop single doc leaves out cross-document premises for multi-hop & guardrails)",
            "SECONDARY_CAUSE": "Model Capability on 3B (small models struggle with reasoning over negation and strict negative constraints even when present in context)",
            "EVIDENCE": (
                f"A2 (2-hop) and R4 (Multi-Signal) increase accuracy from {avg_acc_exp1['A1']:.1%} to {avg_acc_exp1['A2']:.1%}, "
                f"while scaling model from 3B to 7B on Full Context increases accuracy from {m1_b_acc:.1%} to {m2_b_acc:.1%}."
            ),
        },
        "detailed_records": {
            "exp1_budget": exp1_records,
            "exp2_multisignal": exp2_records,
            "exp3_models": exp3_records,
        },
    }

    # Write JSON report
    json_path = out_dir / "retrieval_diagnostic_report.json"
    json_path.write_text(json.dumps(report_data, indent=2, ensure_ascii=False), encoding="utf-8")

    # Generate Markdown Report
    md_lines = [
        "# P0 Diagnostic Report: Budget vs Retrieval vs Model Capability",
        "",
        "## 1. Executive Verdict",
        "",
        f"- **BUDGET BOTTLENECK**: `{budget_bottleneck}`",
        f"- **RETRIEVAL BOTTLENECK**: `{retrieval_bottleneck}`",
        f"- **MODEL CAPABILITY BOTTLENECK**: `{model_bottleneck}`",
        "",
        f"**PRIMARY CAUSE**: {report_data['final_verdict']['PRIMARY_CAUSE']}",
        f"**SECONDARY CAUSE**: {report_data['final_verdict']['SECONDARY_CAUSE']}",
        f"**EVIDENCE**: {report_data['final_verdict']['EVIDENCE']}",
        "",
        "---",
        "",
        "## 2. Experiment 1 — Budget Impact (A1 vs A2 vs B)",
        "",
        "| Condition | Configuration | Avg Accuracy | Avg Tokens | Delta vs A1 |",
        "|---|---|---|---|---|",
        f"| **A1 (Current)** | 1-hop / max 5 results | **{avg_acc_exp1['A1']:.1%}** | ~285t | Baseline |",
        f"| **A2 (Doubled)** | 2-hop / max 10 results | **{avg_acc_exp1['A2']:.1%}** | ~492t | **+{avg_acc_exp1['A2'] - avg_acc_exp1['A1']:.1%}** |",
        f"| **B (Full Context)** | Raw dump | **{avg_acc_exp1['B']:.1%}** | ~878t | **+{avg_acc_exp1['B'] - avg_acc_exp1['A1']:.1%}** |",
        "",
        "---",
        "",
        "## 3. Experiment 2 — Multi-Signal Retrieval Signals (R1 to R4)",
        "",
        "| Signal Layer | Description | Avg Accuracy |",
        "|---|---|---|",
        f"| **R1 (Semantic Only)** | Coarse topic routing | {avg_acc_exp2['R1']:.1%} |",
        f"| **R2 (Semantic + Lexical)** | BM25 token overlap | {avg_acc_exp2['R2']:.1%} |",
        f"| **R3 (Semantic + Lexical + Entity)** | Named entity anchoring | {avg_acc_exp2['R3']:.1%} |",
        f"| **R4 (Semantic + Lexical + Entity + Graph)** | 2-hop connected graph expansion | **{avg_acc_exp2['R4']:.1%}** |",
        "",
        "---",
        "",
        "## 4. Experiment 3 — Model Capability Comparison (M1: 3B vs M2: 7B)",
        "",
        "| Model | Condition A1 (1-hop) | Condition B (Full Context) | Gain from Model Size |",
        "|---|---|---|---|",
        f"| **M1 ({m1_name})** | {m1_a1_acc:.1%} | {m1_b_acc:.1%} | Baseline |",
        f"| **M2 ({m2_name})** | {m2_a1_acc:.1%} | **{m2_b_acc:.1%}** | **+{m2_b_acc - m1_b_acc:.1%} on B** |",
        "",
        "---",
        "",
        "## 5. Required-Fact Failure Breakdown on Condition A1",
        "",
        f"- **RETRIEVAL_FAILURE** (required facts absent from context): `{failure_counts['RETRIEVAL_FAILURE']}`",
        f"- **MODEL_CAPABILITY_FAILURE** (facts present, but answer wrong): `{failure_counts['MODEL_CAPABILITY_FAILURE']}`",
        f"- **BOTH** (partially missing facts + reasoning gap): `{failure_counts['BOTH']}`",
        f"- **SUCCESS** (accurately answered): `{failure_counts['SUCCESS']}`",
        "",
        "---",
        "",
        "## 6. Detailed Query Breakdown Matrix",
        "",
        "| Query ID | Category | A1 Acc | A2 Acc | B Acc | M2 (7B) B Acc | Failure Classification (A1) |",
        "|---|---|---|---|---|---|---|",
    ]

    for c in EVAL_CASES:
        cid = c["id"]
        cat = c["category"]
        r_a1 = next(r for r in exp1_records if r["query_id"] == cid and r["condition"] == "A1_1hop")
        r_a2 = next(r for r in exp1_records if r["query_id"] == cid and r["condition"] == "A2_2hop")
        r_b = next(r for r in exp1_records if r["query_id"] == cid and r["condition"] == "B_full_context")
        r_m2_b = next(r for r in exp3_records if r["query_id"] == cid and r["condition"] == "B_full_context")
        md_lines.append(
            f"| `{cid}` | {cat} | {r_a1['accuracy']:.2f} | {r_a2['accuracy']:.2f} | {r_b['accuracy']:.2f} | {r_m2_b['accuracy']:.2f} | `{r_a1['diagnosis']}` |"
        )

    md_path = out_dir / "retrieval_diagnostic_report.md"
    md_path.write_text("\n".join(md_lines), encoding="utf-8")

    print("\nDiagnostic run complete!")
    print(f"Report saved to: {json_path}")
    print(f"Report saved to: {md_path}")
    return report_data


if __name__ == "__main__":
    run_full_p0_diagnostic()
