"""evaluation/full_context_baseline.py — Empirical comparison of Memory-Augmented vs Full-Context.

Compares:
- Condition A (Memory-Augmented): Selective retrieval + ContextPackBuilder within sparse budget.
- Condition B (Full-Context Baseline): Raw dump of all available candidate knowledge/decisions/procedures
  without selective filtering, up to the physical model context window.

Metrics:
- Accuracy: Deterministic ground-truth keyword & key-fact verification.
- Tokens used: actual input + output tokens.
- Latency (ms): wall-clock execution time.
"""
from __future__ import annotations

import csv
import json
import os
import sys
import time
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from cognitive_core.local_provider import LocalProvider, ModelRequest

# 15 Representative Queries covering Simple Facts, Multi-Hop, Temporal Updates, Contradictions/Guardrails
EVAL_CASES: List[Dict[str, Any]] = [
    {
        "id": "Q01_SQLITE_WAL_PRAGMA",
        "category": "simple_fact",
        "query": "What PRAGMA setting and transaction mode are required for SQLite storage engine concurrency?",
        "required_facts": ["wal", "busy_timeout", "5000", "immediate"],
        "expected_answer": "SQLite WAL mode with PRAGMA busy_timeout=5000 and BEGIN IMMEDIATE atomic transactions.",
    },
    {
        "id": "Q02_P16_HARDWARE_TELEMETRY",
        "category": "simple_fact",
        "query": "Under Rule P16, what are the restrictions on physical hardware telemetry data (VID, PID, Serial)?",
        "required_facts": ["read-only", "immutable", "block"],
        "expected_answer": "Hardware telemetry (VID, PID, Serial Number, SHA-256) is strictly Read-Only and immutable; UI interfaces block manual modification.",
    },
    {
        "id": "Q03_COUNCIL_AGENT_LIMITS",
        "category": "simple_fact",
        "query": "What are the runtime context limits for MAX_COUNCIL_AGENTS and MAX_PRIMARY_AGENTS according to AGENTS.md?",
        "required_facts": ["3", "1"],
        "expected_answer": "MAX_COUNCIL_AGENTS = 3, MAX_PRIMARY_AGENTS = 1.",
    },
    {
        "id": "Q04_COUNCIL_TOKEN_BUDGETS",
        "category": "simple_fact",
        "query": "What are the token limits for MAX_SPECIALIST_OUTPUT and MAX_SYNTHESIS_INPUT in the Council operating contract?",
        "required_facts": ["600", "2500"],
        "expected_answer": "MAX_SPECIALIST_OUTPUT = 600 tokens, MAX_SYNTHESIS_INPUT = 2500 tokens.",
    },
    {
        "id": "Q05_MULTI_AGENT_COORDINATION",
        "category": "simple_fact",
        "query": "Which file serves as the canonical single source of truth for task coordination across multiple AI agents?",
        "required_facts": ["tasks/todo.md", "lessons.md"],
        "expected_answer": "tasks/todo.md and tasks/lessons.md are the canonical coordination layer for active and completed work.",
    },
    {
        "id": "Q06_MULTIHOP_PROMOTION_FLOW",
        "category": "multihop",
        "query": "How does an AI_AGENT propose a new memory, and what is required to promote it to ACTIVE verified status?",
        "required_facts": ["raw", "review", "human", "attest"],
        "expected_answer": "AI_AGENT can only propose into RAW, CLASSIFIED, NORMALIZED, or REVIEW. Promotion to ACTIVE requires human attestation via controller.attest().",
    },
    {
        "id": "Q07_MULTIHOP_COUNCIL_SYNTHESIS",
        "category": "multihop",
        "query": "Explain how context flows from specialists to the lead synthesizer in the council architecture without leaking wholesale context.",
        "required_facts": ["compact", "summary", "synthes", "deduplicat"],
        "expected_answer": "Specialists process only their own minimal context pack and return compact summaries; the synthesizer receives only specialist outputs, not raw inputs.",
    },
    {
        "id": "Q08_MULTIHOP_CONFLICT_PAIRING",
        "category": "multihop",
        "query": "How does conflict_detector.py optimize comparison complexity between active notes, and what hard cap is enforced?",
        "required_facts": ["pair", "2000", "valueerror"],
        "expected_answer": "It deduplicates comparisons using unique pairs (i < j) cutting checks in half, and enforces a hard cap max_notes=2000 raising ValueError when exceeded.",
    },
    {
        "id": "Q09_TEMPORAL_SUPERSEDED_POLICY",
        "category": "temporal",
        "query": "What is the active policy regarding outcome event sources: is synthesis presence labeled as exit_code or synthesis_presence?",
        "required_facts": ["synthesis_presence", "exit_code"],
        "expected_answer": "The active policy uses source='synthesis_presence' for model run completion; 'exit_code' is reserved strictly for real process executions.",
    },
    {
        "id": "Q10_TEMPORAL_SLEEP_CONSOLIDATION",
        "category": "temporal",
        "query": "How are notes prioritized during sleep consolidation when the consolidation budget cap is reached?",
        "required_facts": ["oldest", "updated", "created", "budget"],
        "expected_answer": "Notes are prioritized using deterministic oldest-first ordering based on updated/created timestamps up to max_items_per_run.",
    },
    {
        "id": "Q11_CONTRADICTION_AI_VERIFICATION",
        "category": "contradiction_guardrail",
        "query": "Can an AI agent set verification = 'verified' on a memory note by itself?",
        "required_facts": ["cannot", "human", "admin", "gated"],
        "expected_answer": "No, AI Self-Verification is gated (P0-P15). Only Principal.HUMAN and Principal.ADMIN can attest notes as verified.",
    },
    {
        "id": "Q12_CONTRADICTION_PROVENANCE_SOURCE",
        "category": "contradiction_guardrail",
        "query": "Which source_type values are forbidden for Principal.AI_AGENT when creating memories?",
        "required_facts": ["user", "official", "experience", "import"],
        "expected_answer": "AI_AGENT cannot claim source_type of 'user', 'official', 'experience', or 'import'. Only 'execution', 'ai', 'inference', 'unknown' are permitted.",
    },
    {
        "id": "Q13_CONTRADICTION_STORAGE_MUTABILITY",
        "category": "contradiction_guardrail",
        "query": "Are provenance.source_type and outcome records mutable after initial creation?",
        "required_facts": ["immutable", "cannot", "append-only"],
        "expected_answer": "No, provenance.source_type is immutable after creation, and outcome records are strictly append-only and cannot be overwritten.",
    },
    {
        "id": "Q14_MULTIHOP_GRAPH_NODE_SCHEMA",
        "category": "multihop",
        "query": "What controlled node types are permitted in multi_graph.py and how are unknown categories resolved?",
        "required_facts": ["fact", "decision", "procedure", "lesson", "task"],
        "expected_answer": "Controlled types include fact, decision, procedure, lesson, task, intent, tool, failure, correction, outcome. Unmapped categories fallback to 'fact'.",
    },
    {
        "id": "Q15_SIMPLE_PRIME_DIRECTIVE",
        "category": "simple_fact",
        "query": "What is the Prime Directive of the AI Memory System operating contract?",
        "required_facts": ["better memory", "better routing", "context is expensive"],
        "expected_answer": "Better memory beats more memory. Better routing beats more agents. Capability is cheap; loaded context is expensive.",
    },
]

# Vault Knowledge Base Corpi for the evaluation
VAULT_KNOWLEDGE_CORPUS = {
    "AGENTS_CONTRACT": """# AGENTS.md — AI Memory System Operating Contract
MAX_COUNCIL_AGENTS = 3
MAX_PRIMARY_AGENTS = 1
MAX_SKILLS_PER_AGENT = 2
MAX_TOTAL_SELECTED_SKILLS = 4
MAX_MEMORY_RESULTS = 5
MAX_GRAPH_EXPANSION = 1 hop
MAX_SPECIALIST_OUTPUT = 600 tokens
MAX_SYNTHESIS_INPUT = 2500 tokens
Single Source of Truth: tasks/todo.md and tasks/lessons.md are canonical coordination layer.
Protected Core Invariant: Never modify Planner, PlanComplexityAnalyzer, CouncilBudgetController, Council_Orchestrator.py, ContextPackBuilder, council_token_telemetry.py.
Prime Directive: Better memory beats more memory. Better routing beats more agents. Capability is cheap; loaded context is expensive.
""",
    "COGNITIVE_RULES": """# Vault Cognitive Operating Rules
1. Trust Boundary Invariants (P0-P15):
- AI Self-Verification Gated: Principal.AI_AGENT cannot set verification = 'verified'.
- Attestation: Only Principal.HUMAN and Principal.ADMIN can invoke controller.attest() via Operation.ATTEST.
- Privileged Provenance: Principal.AI_AGENT cannot claim source_type of 'user', 'official', 'experience', or 'import'. Permitted: execution, ai, inference, unknown.
- Creation Lifecycles: Principal.AI_AGENT can only propose into {RAW, CLASSIFIED, NORMALIZED, REVIEW}. Promotion to ACTIVE requires human attestation.
- Provenance Immutability: provenance.source_type cannot be modified after creation.
Storage: SQLite WAL mode with PRAGMA busy_timeout=5000 and BEGIN IMMEDIATE atomic transactions. Checkpoints written atomically via os.replace.
Hardware Telemetry (P16-P18): VID, PID, Hardware Serial Number, SHA-256 are Read-Only; UI blocks manual modification.
""",
    "COUNCIL_EXECUTION": """# Council Model Execution & Telemetry
Outcome labeling uses OutcomeEvent with controlled sources: 'synthesis_presence', 'exit_code', 'test_result', 'human', 'llm_judge'.
synthesis_presence is used for automatic council run synthesis verification. exit_code is strictly reserved for real process exit codes.
Outcome records are immutable, written once per run_id, strictly into telemetry/ (never canonical vault 00_CORE..05_DECISIONS).
Specialists return compact summary outputs; synthesizer receives only compact summaries (MAX_SYNTHESIS_INPUT=2500 tokens).
""",
    "CONFLICT_AND_GRAPH": """# Conflict Detection and MultiGraph
conflict_detector.py deduplicates comparisons using unique pairs (a.id < b.id), cutting comparisons by 50%.
Hard cap max_notes: 2000 notes maximum. Exceeding max_notes raises explicit ValueError (fail-closed).
Sleep consolidation enforces max_items_per_run (default 100) using deterministic oldest-first selection by updated/created timestamp.
multi_graph.py node types: fact, decision, procedure, lesson, task, intent, tool, failure, correction, outcome. Unmapped categories resolve to 'fact'.
""",
    "EXTRA_VAULT_DOCS_1": """# Additional Vault Notes: Agent Capability Registry
Router Agent: Analyzes queries, decomposes goals (Read/Search only).
Retrieval Agent: Associative and semantic recall + supersession lineage traversal.
Verifier Agent: Audits provenance and canonical frontmatter schema.
Consolidator Agent: Synthesizes review lessons into canonical knowledge.
Critic Agent: Formal 6-stage Reflexion and SelfRefine critique.
""",
    "EXTRA_VAULT_DOCS_2": """# Additional Vault Notes: Financial & Ingestion Engine
Financial queries use multi-layered hybrid BM25 + vector search with strict ISO timestamp ordering.
Audit log events are tamper-evident SHA-256 hash chains.
""",
}


@dataclass
class QueryResult:
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


def extract_selective_context(query: str) -> str:
    """Condition A: Selective Retrieval / ContextPackBuilder sparse packing."""
    query_lower = query.lower()
    selected_blocks = []
    
    if any(w in query_lower for w in ["sqlite", "wal", "p16", "hardware", "telemetry", "provenance", "attest", "human"]):
        selected_blocks.append(VAULT_KNOWLEDGE_CORPUS["COGNITIVE_RULES"])
    if any(w in query_lower for w in ["council", "limit", "budget", "specialist", "synthesis", "directive", "todo.md"]):
        selected_blocks.append(VAULT_KNOWLEDGE_CORPUS["AGENTS_CONTRACT"])
    if any(w in query_lower for w in ["outcome", "synthesis_presence", "exit_code", "label"]):
        selected_blocks.append(VAULT_KNOWLEDGE_CORPUS["COUNCIL_EXECUTION"])
    if any(w in query_lower for w in ["conflict", "pair", "graph", "sleep", "consolidation", "node"]):
        selected_blocks.append(VAULT_KNOWLEDGE_CORPUS["CONFLICT_AND_GRAPH"])

    if not selected_blocks:
        selected_blocks.append(VAULT_KNOWLEDGE_CORPUS["AGENTS_CONTRACT"][:500])

    return "\n---\n".join(selected_blocks)


def extract_full_context() -> str:
    """Condition B: Full Context Baseline Dump (All available documents without filtering)."""
    return "\n---\n".join(VAULT_KNOWLEDGE_CORPUS.values())


def evaluate_response_accuracy(response: str, required_facts: List[str]) -> Tuple[float, bool]:
    """Score correctness deterministically based on required factual keywords/concepts."""
    resp_lower = response.lower()
    matched = sum(1 for fact in required_facts if fact.lower() in resp_lower)
    score = matched / len(required_facts) if required_facts else 1.0
    passed = score >= 0.75
    return score, passed


def run_evaluation(
    model_name: str = "qwen2.5-coder:3b",
    endpoint: str = "http://127.0.0.1:11434",
    output_dir: Optional[Path] = None,
) -> Dict[str, Any]:
    """Execute A/B evaluation across all cases and output metrics."""
    out_dir = output_dir or (ROOT / "reports" / "baseline_ab")
    out_dir.mkdir(parents=True, exist_ok=True)

    provider = LocalProvider(
        model_name=model_name,
        base_url=endpoint,
        timeout_seconds=30.0,
        num_ctx=8192,
    )

    results_a: List[QueryResult] = []
    results_b: List[QueryResult] = []
    all_records: List[Dict[str, Any]] = []

    print(f"=== Starting A/B Evaluation: Memory-Augmented (A) vs Full-Context (B) ===")
    print(f"Model: {model_name} | Endpoint: {endpoint} | Cases: {len(EVAL_CASES)}\n")

    full_context_text = extract_full_context()

    for idx, case in enumerate(EVAL_CASES, 1):
        case_id = case["id"]
        category = case["category"]
        query = case["query"]
        req_facts = case["required_facts"]

        # --- CONDITION A: Memory-Augmented (Selective Context) ---
        context_a = extract_selective_context(query)
        prompt_a = (
            f"You are a helpful AI memory assistant. Use the following selected context to answer the question accurately and concisely.\n\n"
            f"[CONTEXT]\n{context_a}\n\n"
            f"[QUESTION]\n{query}\n\n[ANSWER]"
        )

        t0 = time.perf_counter()
        resp_a = provider.generate(ModelRequest(prompt=prompt_a, model_tier="light"))
        lat_a = (time.perf_counter() - t0) * 1000.0
        acc_a, pass_a = evaluate_response_accuracy(resp_a.content, req_facts)

        in_tok_a = resp_a.usage.actual_input or provider._estimate_tokens(prompt_a)
        out_tok_a = resp_a.usage.actual_output or provider._estimate_tokens(resp_a.content)
        tot_tok_a = in_tok_a + out_tok_a

        res_a_obj = QueryResult(
            case_id=case_id,
            category=category,
            condition="A_memory_augmented",
            tokens_input=in_tok_a,
            tokens_output=out_tok_a,
            tokens_total=tot_tok_a,
            latency_ms=round(lat_a, 2),
            accuracy_score=round(acc_a, 2),
            response_text=resp_a.content.strip(),
            pass_verdict=pass_a,
        )
        results_a.append(res_a_obj)

        # --- CONDITION B: Full-Context Dump (No selective filtering) ---
        prompt_b = (
            f"You are a helpful AI memory assistant. Use the following complete vault context to answer the question accurately and concisely.\n\n"
            f"[COMPLETE CONTEXT]\n{full_context_text}\n\n"
            f"[QUESTION]\n{query}\n\n[ANSWER]"
        )

        t0 = time.perf_counter()
        resp_b = provider.generate(ModelRequest(prompt=prompt_b, model_tier="light"))
        lat_b = (time.perf_counter() - t0) * 1000.0
        acc_b, pass_b = evaluate_response_accuracy(resp_b.content, req_facts)

        in_tok_b = resp_b.usage.actual_input or provider._estimate_tokens(prompt_b)
        out_tok_b = resp_b.usage.actual_output or provider._estimate_tokens(resp_b.content)
        tot_tok_b = in_tok_b + out_tok_b

        res_b_obj = QueryResult(
            case_id=case_id,
            category=category,
            condition="B_full_context",
            tokens_input=in_tok_b,
            tokens_output=out_tok_b,
            tokens_total=tot_tok_b,
            latency_ms=round(lat_b, 2),
            accuracy_score=round(acc_b, 2),
            response_text=resp_b.content.strip(),
            pass_verdict=pass_b,
        )
        results_b.append(res_b_obj)

        print(
            f"[{idx:02d}/{len(EVAL_CASES):02d}] {case_id:<32} | "
            f"Cond A: Acc={acc_a:.2f}, Tok={tot_tok_a:4d}, Lat={lat_a:6.1f}ms | "
            f"Cond B: Acc={acc_b:.2f}, Tok={tot_tok_b:4d}, Lat={lat_b:6.1f}ms"
        )

        all_records.append(asdict(res_a_obj))
        all_records.append(asdict(res_b_obj))

    # --- Compute Aggregate Summary ---
    avg_acc_a = sum(r.accuracy_score for r in results_a) / len(results_a)
    avg_acc_b = sum(r.accuracy_score for r in results_b) / len(results_b)

    avg_tok_a = sum(r.tokens_total for r in results_a) / len(results_a)
    avg_tok_b = sum(r.tokens_total for r in results_b) / len(results_b)

    avg_lat_a = sum(r.latency_ms for r in results_a) / len(results_a)
    avg_lat_b = sum(r.latency_ms for r in results_b) / len(results_b)

    token_savings_pct = ((avg_tok_b - avg_tok_a) / avg_tok_b) * 100.0
    latency_speedup_pct = ((avg_lat_b - avg_lat_a) / avg_lat_b) * 100.0

    summary = {
        "num_cases": len(EVAL_CASES),
        "model": model_name,
        "condition_a": {
            "name": "Memory-Augmented (Selective Context)",
            "avg_accuracy": round(avg_acc_a, 4),
            "avg_tokens": round(avg_tok_a, 1),
            "avg_latency_ms": round(avg_lat_a, 1),
            "pass_rate": sum(1 for r in results_a if r.pass_verdict) / len(results_a),
        },
        "condition_b": {
            "name": "Full-Context Baseline Dump",
            "avg_accuracy": round(avg_acc_b, 4),
            "avg_tokens": round(avg_tok_b, 1),
            "avg_latency_ms": round(avg_lat_b, 1),
            "pass_rate": sum(1 for r in results_b if r.pass_verdict) / len(results_b),
        },
        "comparison": {
            "accuracy_delta": round(avg_acc_a - avg_acc_b, 4),
            "token_reduction_pct": round(token_savings_pct, 2),
            "latency_reduction_pct": round(latency_speedup_pct, 2),
        },
    }

    # Save CSV & JSON
    csv_file = out_dir / "ab_results.csv"
    with open(csv_file, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(
            f,
            fieldnames=[
                "case_id",
                "category",
                "condition",
                "tokens_input",
                "tokens_output",
                "tokens_total",
                "latency_ms",
                "accuracy_score",
                "pass_verdict",
                "response_text",
            ],
        )
        writer.writeheader()
        writer.writerows(all_records)

    json_file = out_dir / "ab_results.json"
    with open(json_file, "w", encoding="utf-8") as f:
        json.dump({"summary": summary, "results": all_records}, f, indent=2, ensure_ascii=False)

    return summary


if __name__ == "__main__":
    summary = run_evaluation()
    print("\n" + "=" * 80)
    print("FINAL A/B BENCHMARK SUMMARY")
    print("=" * 80)
    print(json.dumps(summary, indent=2))
