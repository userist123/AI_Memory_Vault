"""evaluation/retrieval_diagnostic_a1_a2_b.py — Diagnostic Harness: A1 vs A2 vs B.

Compares:
- Condition A1: Standard sparse retrieval (1-hop expansion, max 5 memory results / primary doc).
- Condition A2: Expanded retrieval (2-hop expansion, max 10 memory results / multi-boundary cross-docs).
- Condition B: Full-context baseline dump.

Diagnostic Target:
- Determine if accuracy loss in Q06, Q07, Q08, Q11, Q12, Q13 is due to:
  a) Budget too tight (recovered by A2) -> Budget expansion fix.
  b) Selection/routing blind spot (missed even with A2) -> Multi-signal retrieval need.
"""
from __future__ import annotations

import csv
import json
import sys
import time
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from cognitive_core.local_provider import LocalProvider, ModelRequest
from evaluation.full_context_baseline import EVAL_CASES, VAULT_KNOWLEDGE_CORPUS, evaluate_response_accuracy, extract_full_context


@dataclass
class DiagnosticResult:
    case_id: str
    category: str
    condition: str
    tokens_input: int
    tokens_output: int
    tokens_total: int
    latency_ms: float
    accuracy_score: float
    response_text: str
    pass_verdict: bool


def extract_context_a1(query: str) -> str:
    """A1: 1-hop strict sparse retrieval (Single primary matching topic)."""
    q = query.lower()
    selected_blocks = []

    # Strict 1-hop matching
    if any(w in q for w in ["sqlite", "wal", "p16", "hardware", "telemetry"]):
        selected_blocks.append(VAULT_KNOWLEDGE_CORPUS["COGNITIVE_RULES"])
    elif any(w in q for w in ["council", "limit", "budget", "specialist", "synthesis", "directive", "todo.md"]):
        selected_blocks.append(VAULT_KNOWLEDGE_CORPUS["AGENTS_CONTRACT"])
    elif any(w in q for w in ["outcome", "synthesis_presence", "exit_code"]):
        selected_blocks.append(VAULT_KNOWLEDGE_CORPUS["COUNCIL_EXECUTION"])
    elif any(w in q for w in ["conflict", "pair", "graph", "sleep", "consolidation", "node"]):
        selected_blocks.append(VAULT_KNOWLEDGE_CORPUS["CONFLICT_AND_GRAPH"])
    elif any(w in q for w in ["ai agent", "provenance", "attest", "human"]):
        # A1 1-hop might only retrieve AGENTS_CONTRACT based on agent role
        selected_blocks.append(VAULT_KNOWLEDGE_CORPUS["AGENTS_CONTRACT"])
    else:
        selected_blocks.append(VAULT_KNOWLEDGE_CORPUS["AGENTS_CONTRACT"][:500])

    return "\n---\n".join(selected_blocks)


def extract_context_a2(query: str) -> str:
    """A2: 2-hop expanded multi-boundary retrieval (Expands 1-hop graph neighbors & secondary cross-docs)."""
    q = query.lower()
    selected_blocks = []

    # 2-hop graph expansion includes primary + connected policy/governance docs
    if any(w in q for w in ["sqlite", "wal", "p16", "hardware", "telemetry", "provenance", "attest", "human", "verification"]):
        selected_blocks.append(VAULT_KNOWLEDGE_CORPUS["COGNITIVE_RULES"])
        selected_blocks.append(VAULT_KNOWLEDGE_CORPUS["AGENTS_CONTRACT"])
    if any(w in q for w in ["council", "limit", "budget", "specialist", "synthesis", "directive", "todo.md"]):
        selected_blocks.append(VAULT_KNOWLEDGE_CORPUS["AGENTS_CONTRACT"])
        selected_blocks.append(VAULT_KNOWLEDGE_CORPUS["COUNCIL_EXECUTION"])
    if any(w in q for w in ["outcome", "synthesis_presence", "exit_code", "label", "mutability"]):
        selected_blocks.append(VAULT_KNOWLEDGE_CORPUS["COUNCIL_EXECUTION"])
        selected_blocks.append(VAULT_KNOWLEDGE_CORPUS["COGNITIVE_RULES"])
    if any(w in q for w in ["conflict", "pair", "graph", "sleep", "consolidation", "node"]):
        selected_blocks.append(VAULT_KNOWLEDGE_CORPUS["CONFLICT_AND_GRAPH"])
        selected_blocks.append(VAULT_KNOWLEDGE_CORPUS["COGNITIVE_RULES"])

    if not selected_blocks:
        selected_blocks = [VAULT_KNOWLEDGE_CORPUS["AGENTS_CONTRACT"], VAULT_KNOWLEDGE_CORPUS["COGNITIVE_RULES"]]

    # Deduplicate blocks preserving order
    seen = set()
    deduped = []
    for b in selected_blocks:
        if b not in seen:
            seen.add(b)
            deduped.append(b)

    return "\n---\n".join(deduped)


def run_diagnostic(
    model_name: str = "qwen2.5-coder:3b",
    endpoint: str = "http://127.0.0.1:11434",
    output_dir: Optional[Path] = None,
) -> Dict[str, Any]:
    out_dir = output_dir or (ROOT / "reports" / "baseline_ab")
    out_dir.mkdir(parents=True, exist_ok=True)

    provider = LocalProvider(
        model_name=model_name,
        base_url=endpoint,
        timeout_seconds=30.0,
        num_ctx=8192,
    )

    results_a1: List[DiagnosticResult] = []
    results_a2: List[DiagnosticResult] = []
    results_b: List[DiagnosticResult] = []
    all_records: List[Dict[str, Any]] = []

    full_context_text = extract_full_context()

    print(f"=== Starting Diagnostic: A1 (1-hop) vs A2 (2-hop) vs B (Full Context) ===")
    print(f"Model: {model_name} | Endpoint: {endpoint} | Cases: {len(EVAL_CASES)}\n")

    for idx, case in enumerate(EVAL_CASES, 1):
        case_id = case["id"]
        category = case["category"]
        query = case["query"]
        req_facts = case["required_facts"]

        # --- CONDITION A1: 1-hop strict sparse ---
        ctx_a1 = extract_context_a1(query)
        prompt_a1 = (
            f"You are a helpful AI memory assistant. Use the following selected context to answer the question accurately and concisely.\n\n"
            f"[CONTEXT]\n{ctx_a1}\n\n"
            f"[QUESTION]\n{query}\n\n[ANSWER]"
        )
        t0 = time.perf_counter()
        resp_a1 = provider.generate(ModelRequest(prompt=prompt_a1, model_tier="light"))
        lat_a1 = (time.perf_counter() - t0) * 1000.0
        acc_a1, pass_a1 = evaluate_response_accuracy(resp_a1.content, req_facts)
        tok_a1 = (resp_a1.usage.actual_input or provider._estimate_tokens(prompt_a1)) + (resp_a1.usage.actual_output or provider._estimate_tokens(resp_a1.content))

        r_a1 = DiagnosticResult(
            case_id=case_id, category=category, condition="A1_1hop",
            tokens_input=resp_a1.usage.actual_input or 0, tokens_output=resp_a1.usage.actual_output or 0,
            tokens_total=tok_a1, latency_ms=round(lat_a1, 2), accuracy_score=round(acc_a1, 2),
            response_text=resp_a1.content.strip(), pass_verdict=pass_a1,
        )
        results_a1.append(r_a1)

        # --- CONDITION A2: 2-hop expanded cross-docs ---
        ctx_a2 = extract_context_a2(query)
        prompt_a2 = (
            f"You are a helpful AI memory assistant. Use the following selected context to answer the question accurately and concisely.\n\n"
            f"[CONTEXT]\n{ctx_a2}\n\n"
            f"[QUESTION]\n{query}\n\n[ANSWER]"
        )
        t0 = time.perf_counter()
        resp_a2 = provider.generate(ModelRequest(prompt=prompt_a2, model_tier="light"))
        lat_a2 = (time.perf_counter() - t0) * 1000.0
        acc_a2, pass_a2 = evaluate_response_accuracy(resp_a2.content, req_facts)
        tok_a2 = (resp_a2.usage.actual_input or provider._estimate_tokens(prompt_a2)) + (resp_a2.usage.actual_output or provider._estimate_tokens(resp_a2.content))

        r_a2 = DiagnosticResult(
            case_id=case_id, category=category, condition="A2_2hop",
            tokens_input=resp_a2.usage.actual_input or 0, tokens_output=resp_a2.usage.actual_output or 0,
            tokens_total=tok_a2, latency_ms=round(lat_a2, 2), accuracy_score=round(acc_a2, 2),
            response_text=resp_a2.content.strip(), pass_verdict=pass_a2,
        )
        results_a2.append(r_a2)

        # --- CONDITION B: Full Context Dump ---
        prompt_b = (
            f"You are a helpful AI memory assistant. Use the following complete vault context to answer the question accurately and concisely.\n\n"
            f"[COMPLETE CONTEXT]\n{full_context_text}\n\n"
            f"[QUESTION]\n{query}\n\n[ANSWER]"
        )
        t0 = time.perf_counter()
        resp_b = provider.generate(ModelRequest(prompt=prompt_b, model_tier="light"))
        lat_b = (time.perf_counter() - t0) * 1000.0
        acc_b, pass_b = evaluate_response_accuracy(resp_b.content, req_facts)
        tok_b = (resp_b.usage.actual_input or provider._estimate_tokens(prompt_b)) + (resp_b.usage.actual_output or provider._estimate_tokens(resp_b.content))

        r_b = DiagnosticResult(
            case_id=case_id, category=category, condition="B_full_context",
            tokens_input=resp_b.usage.actual_input or 0, tokens_output=resp_b.usage.actual_output or 0,
            tokens_total=tok_b, latency_ms=round(lat_b, 2), accuracy_score=round(acc_b, 2),
            response_text=resp_b.content.strip(), pass_verdict=pass_b,
        )
        results_b.append(r_b)

        print(
            f"[{idx:02d}/{len(EVAL_CASES):02d}] {case_id:<32} | "
            f"A1: Acc={acc_a1:.2f} ({tok_a1:3d}t) | "
            f"A2: Acc={acc_a2:.2f} ({tok_a2:3d}t) | "
            f"B: Acc={acc_b:.2f} ({tok_b:3d}t)"
        )

        all_records.extend([asdict(r_a1), asdict(r_a2), asdict(r_b)])

    # Aggregates
    avg_acc_a1 = sum(r.accuracy_score for r in results_a1) / len(results_a1)
    avg_acc_a2 = sum(r.accuracy_score for r in results_a2) / len(results_a2)
    avg_acc_b = sum(r.accuracy_score for r in results_b) / len(results_b)

    avg_tok_a1 = sum(r.tokens_total for r in results_a1) / len(results_a1)
    avg_tok_a2 = sum(r.tokens_total for r in results_a2) / len(results_a2)
    avg_tok_b = sum(r.tokens_total for r in results_b) / len(results_b)

    diag_summary = {
        "num_cases": len(EVAL_CASES),
        "model": model_name,
        "a1_1hop": {
            "avg_accuracy": round(avg_acc_a1, 4),
            "avg_tokens": round(avg_tok_a1, 1),
            "pass_rate": round(sum(1 for r in results_a1 if r.pass_verdict) / len(results_a1), 4),
        },
        "a2_2hop": {
            "avg_accuracy": round(avg_acc_a2, 4),
            "avg_tokens": round(avg_tok_a2, 1),
            "pass_rate": round(sum(1 for r in results_a2 if r.pass_verdict) / len(results_a2), 4),
        },
        "b_full_context": {
            "avg_accuracy": round(avg_acc_b, 4),
            "avg_tokens": round(avg_tok_b, 1),
            "pass_rate": round(sum(1 for r in results_b if r.pass_verdict) / len(results_b), 4),
        },
        "deltas": {
            "a2_vs_a1_accuracy_gain": round(avg_acc_a2 - avg_acc_a1, 4),
            "b_vs_a2_accuracy_gap": round(avg_acc_b - avg_acc_a2, 4),
            "a2_token_savings_vs_b_pct": round(((avg_tok_b - avg_tok_a2) / avg_tok_b) * 100.0, 2),
        },
    }

    # Save to JSON & CSV
    csv_file = out_dir / "diagnostic_a1_a2_b_results.csv"
    with open(csv_file, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(
            f,
            fieldnames=[
                "case_id", "category", "condition", "tokens_input", "tokens_output",
                "tokens_total", "latency_ms", "accuracy_score", "pass_verdict", "response_text"
            ]
        )
        writer.writeheader()
        writer.writerows(all_records)

    json_file = out_dir / "diagnostic_a1_a2_b_results.json"
    with open(json_file, "w", encoding="utf-8") as f:
        json.dump({"summary": diag_summary, "results": all_records}, f, indent=2, ensure_ascii=False)

    return diag_summary


if __name__ == "__main__":
    summary = run_diagnostic()
    print("\n" + "=" * 80)
    print("DIAGNOSTIC SUMMARY: A1 vs A2 vs B")
    print("=" * 80)
    print(json.dumps(summary, indent=2))
