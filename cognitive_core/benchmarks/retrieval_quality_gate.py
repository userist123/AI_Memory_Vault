"""cognitive_core/benchmarks/retrieval_quality_gate.py — P2.3 Retrieval Quality Gate.

Transforms P1.7 / P2 retrieval benchmarks into a formal, reproducible Quality Gate
with explicit target thresholds, mathematical justifications, retrieval impacts,
and PASS / WARN / FAIL statuses.

Evaluates 7 core dimensions:
1. Known-Item Retrieval (Recall@10 >= 0.85, MRR >= 0.55)
2. Paraphrase Retrieval (Recall@10 >= 0.60, MRR >= 0.40, graceful offline handling)
3. Multi-Hop Retrieval (Rescue Rate >= 25%, Net Gain > 0)
4. Entity-Mediated Retrieval (Recall@10 >= 0.35)
5. Security Filtering (0 leaks: 100% compliant with ACTIVE + verified ceiling)
6. Determinism (100% identical rank ordering across runs)
7. Latency (Median < 5.0 ms)

Zero storage mutation. Zero runtime controller modification. Fail-closed.
"""
from __future__ import annotations

import argparse
import json
import math
import sys
import time
from collections import Counter
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Optional, Set, Tuple

from ..hybrid_retrieval import Hit, HybridRetriever, tokenize
from ..retrieval_boundary import (
    BoundaryViolationError,
    FilterValidationError,
    RetrievalBoundaryAdapter,
)
from ..retrieval_facade import (
    FacadeNoteResult,
    FacadeRetrievalRequest,
    ProductionRetrievalFacade,
)
from ..vault_index import Note, VaultIndex
from .metrics import mean_reciprocal_rank, recall_at_k
from .multi_hop_evaluator import (
    CorpusGraph,
    MultiHopEvaluator,
    ProbeCase,
    ProbeResult,
    check_synapse_infrastructure,
)
from .retrieval_evaluation_p17 import (
    check_ollama_generate_available,
    evaluate_known_item_facade,
    evaluate_paraphrase_retrieval,
)


def _ensure_utf8_streams() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass


@dataclass
class GateMetricResult:
    metric_id: str
    dimension: str
    description: str
    justification: str
    retrieval_impact: str
    measured_value: Any
    target_threshold: str
    status: str  # "PASS", "WARN", "FAIL"


@dataclass
class RetrievalQualityGateReport:
    timestamp_utc: str
    vault_root: str
    overall_verdict: str  # "PASS", "CONDITIONAL_PASS", "FAIL"
    summary: Dict[str, int]
    metrics: List[GateMetricResult]
    synapse_infrastructure_status: str


def run_retrieval_quality_gate(
    vault_root: Path | str = ".",
    sample_known_item: int = 50,
    sample_paraphrase: int = 15,
    sample_multi_hop_per_type: int = 30,
) -> RetrievalQualityGateReport:
    """Executes all retrieval benchmarks and validates them against formal thresholds."""
    root = Path(vault_root)
    idx = VaultIndex.load(root)
    retriever = HybridRetriever(idx)
    adapter = RetrievalBoundaryAdapter(retriever)
    facade = ProductionRetrievalFacade(adapter=adapter)

    metrics: List[GateMetricResult] = []

    # -----------------------------------------------------------------------
    # Dimension 1: Known-Item Retrieval
    # Target: Recall@10 >= 0.85, MRR >= 0.55
    # -----------------------------------------------------------------------
    ki_res = evaluate_known_item_facade(idx, sample_size=sample_known_item, seed=42)
    ki_r10 = ki_res.get("recall@10", 0.0)
    ki_mrr = ki_res.get("mrr", 0.0)
    ki_med_lat = ki_res.get("median_latency_ms", 0.0)
    ki_det = ki_res.get("deterministic", True)

    metrics.append(
        GateMetricResult(
            metric_id="QG-KI-01",
            dimension="Known-Item Retrieval",
            description="Recall@10 for title and body snippet queries on canonical notes",
            justification="Agents must find relevant canonical memories when querying with explicit terminology.",
            retrieval_impact="Low recall causes agents to hallucinate or miss stored procedures and facts.",
            measured_value=ki_r10,
            target_threshold=">= 0.8500 (>= 85%)",
            status="PASS" if ki_r10 >= 0.85 else "FAIL",
        )
    )

    metrics.append(
        GateMetricResult(
            metric_id="QG-KI-02",
            dimension="Known-Item Retrieval",
            description="MRR (Mean Reciprocal Rank) on known-item queries",
            justification="Canonical target note should rank as close to rank 1 as possible.",
            retrieval_impact="Low MRR forces agents to process unnecessary context tokens from lower-ranked items.",
            measured_value=ki_mrr,
            target_threshold=">= 0.5500 (>= 55%)",
            status="PASS" if ki_mrr >= 0.55 else "FAIL",
        )
    )

    # -----------------------------------------------------------------------
    # Dimension 2: Paraphrase Retrieval
    # Target: Recall@10 >= 0.60, MRR >= 0.40 (Warn if Ollama offline)
    # -----------------------------------------------------------------------
    para_res = evaluate_paraphrase_retrieval(idx, sample_size=sample_paraphrase, seed=42)
    if para_res.get("status") == "OK":
        para_r10 = para_res.get("recall@10", 0.0)
        para_mrr = para_res.get("mrr", 0.0)
        para_status = "PASS" if (para_r10 >= 0.60 and para_mrr >= 0.40) else "WARN"
        para_val = f"Recall@10={para_r10}, MRR={para_mrr}"
    else:
        para_status = "WARN"
        para_val = "PROVIDER_OFFLINE (Local Ollama qwen2.5-coder:3b unavailable)"

    metrics.append(
        GateMetricResult(
            metric_id="QG-PARA-01",
            dimension="Paraphrase Retrieval",
            description="Semantic recall under linguistic variation and reformulation",
            justification="Conversational queries rarely match exact note titles verbatim.",
            retrieval_impact="Vocabulary mismatch degrades lexical recall without semantic or associative support.",
            measured_value=para_val,
            target_threshold="Recall@10 >= 0.60, MRR >= 0.40 (or WARN if LLM offline)",
            status=para_status,
        )
    )

    # -----------------------------------------------------------------------
    # Dimension 3 & 4: Multi-Hop & Entity-Mediated Retrieval
    # Target: Rescue Rate >= 25%, Net Gain > 0, Entity Recall@10 >= 0.35
    # -----------------------------------------------------------------------
    evaluator = MultiHopEvaluator(idx, retriever=retriever)
    mh_report = evaluator.evaluate(max_cases_per_type=sample_multi_hop_per_type)

    metrics.append(
        GateMetricResult(
            metric_id="QG-MH-01",
            dimension="Multi-Hop Retrieval",
            description="Graph Rescue Rate (cases where direct search missed but graph expansion succeeded)",
            justification="Graph expansion must rescue relevant notes disconnected from surface query keywords.",
            retrieval_impact="Low rescue rate indicates graph expansion adds latency without discovering new answers.",
            measured_value=round(mh_report.rescue_rate, 4),
            target_threshold=">= 0.2500 (>= 25%)",
            status="PASS" if mh_report.rescue_rate >= 0.25 else "WARN",
        )
    )

    metrics.append(
        GateMetricResult(
            metric_id="QG-MH-02",
            dimension="Multi-Hop Retrieval",
            description="Net Gain (rescued relevant items minus false positive expansions)",
            justification="Expansion must produce more rescued relevant items than irrelevant clutter.",
            retrieval_impact="Negative net gain floods council context window with noise.",
            measured_value=mh_report.net_gain,
            target_threshold="> 0 (positive net gain)",
            status="PASS" if mh_report.net_gain > 0 else "FAIL",
        )
    )

    # Entity-mediated breakdown
    entity_metrics = mh_report.modality_breakdown.get("entity", {})
    entity_recall = entity_metrics.get("multi_hop_recall", 0.0)

    metrics.append(
        GateMetricResult(
            metric_id="QG-ENT-01",
            dimension="Entity-Mediated Retrieval",
            description="Multi-hop recall over shared technical entity / tag bridges",
            justification="Cross-domain knowledge items often connect via shared architectural entities.",
            retrieval_impact="Failure to follow entity bridges leaves associated components isolated.",
            measured_value=round(entity_recall, 4),
            target_threshold=">= 0.3500 (>= 35%)",
            status="PASS" if entity_recall >= 0.35 else "WARN",
        )
    )

    # -----------------------------------------------------------------------
    # Dimension 5: Security Filtering (0 leaks across ACTIVE + verified ceiling)
    # -----------------------------------------------------------------------
    # Test multiple queries to verify security boundary
    security_leaks = 0
    test_queries = [
        "memory", "security", "governance", "test", "lesson",
        "policy", "architecture", "agent", "sqlite", "wal",
    ]
    for q in test_queries:
        req = FacadeRetrievalRequest(query=q, principal="human", page_size=20, request_id="sec-audit")
        resp = facade.retrieve(req)
        for r in resp.results:
            note = idx.by_id.get(r.id)
            if not note:
                continue
            if note.lifecycle != "ACTIVE" or note.verification != "verified":
                security_leaks += 1

    # Also test malicious attempt to supply non-active / unverified filter (must fail-closed)
    try:
        attempt_req = FacadeRetrievalRequest(
            query="test",
            principal="human",
            page_size=20,
            lifecycles=["REVIEW", "RAW"],
            verification=["unverified"],
        )
        attempt_resp = facade.retrieve(attempt_req)
        for r in attempt_resp.results:
            note = idx.by_id.get(r.id)
            if note and (note.lifecycle != "ACTIVE" or note.verification != "verified"):
                security_leaks += 1
    except (BoundaryViolationError, FilterValidationError):
        # Fail-closed rejection is the desired and compliant behavior
        pass

    metrics.append(
        GateMetricResult(
            metric_id="QG-SEC-01",
            dimension="Security Trust Boundary",
            description="Strict compliance with ACTIVE + verified ceiling (0 leaks allowed)",
            justification="Invariants I-001..I-012 prohibit unverified or in-review notes from entering council runtime.",
            retrieval_impact="Security leak allows untrusted external imports or prompt injections into agent context.",
            measured_value=security_leaks,
            target_threshold="== 0 leaks (100% compliance)",
            status="PASS" if security_leaks == 0 else "FAIL",
        )
    )

    # -----------------------------------------------------------------------
    # Dimension 6: Determinism (100% ordering & score stability)
    # -----------------------------------------------------------------------
    metrics.append(
        GateMetricResult(
            metric_id="QG-DET-01",
            dimension="Retrieval Determinism",
            description="100% identical ranking and score parity across repeated queries",
            justification="Reproducibility is essential for cognitive debugging, test suites, and audit logs.",
            retrieval_impact="Non-deterministic results produce flaky agent decisions and unreproducible failures.",
            measured_value=ki_det and mh_report.deterministic,
            target_threshold="== True (100% deterministic)",
            status="PASS" if (ki_det and mh_report.deterministic) else "FAIL",
        )
    )

    # -----------------------------------------------------------------------
    # Dimension 7: Latency
    # Target: Median < 5.0 ms
    # -----------------------------------------------------------------------
    metrics.append(
        GateMetricResult(
            metric_id="QG-PERF-01",
            dimension="Execution Performance",
            description="Median retrieval latency through ProductionRetrievalFacade",
            justification="Context assembly runs in the critical cognitive path before council agent execution.",
            retrieval_impact="High latency slows multi-agent turn cycles and creates UI sluggishness.",
            measured_value=round(ki_med_lat, 3),
            target_threshold="< 5.000 ms",
            status="PASS" if ki_med_lat < 5.0 else "WARN",
        )
    )

    # Summary
    status_counts = Counter(m.status for m in metrics)
    if status_counts["FAIL"] > 0:
        overall_verdict = "FAIL"
    elif status_counts["WARN"] > 0:
        overall_verdict = "CONDITIONAL_PASS"
    else:
        overall_verdict = "PASS"

    syn_infra = check_synapse_infrastructure(vault_root)

    return RetrievalQualityGateReport(
        timestamp_utc=datetime.now(timezone.utc).isoformat(),
        vault_root=str(root.resolve()),
        overall_verdict=overall_verdict,
        summary=dict(status_counts),
        metrics=metrics,
        synapse_infrastructure_status=syn_infra.status,
    )


def main() -> None:
    _ensure_utf8_streams()
    parser = argparse.ArgumentParser(description="P2.3 Retrieval Quality Gate Evaluator")
    parser.add_argument("--vault-root", default=".", help="Root path of the vault")
    parser.add_argument(
        "--output",
        default="07_EVALUATION/ci_evidence/retrieval_quality_gate_report.json",
        help="Destination path for JSON report",
    )
    args = parser.parse_args()

    print("[*] Running P2.3 Retrieval Quality Gate...")
    report = run_retrieval_quality_gate(args.vault_root)

    out_p = Path(args.output)
    out_p.parent.mkdir(parents=True, exist_ok=True)
    with open(out_p, "w", encoding="utf-8") as f:
        json.dump(asdict(report), f, indent=2, ensure_ascii=False)

    print(f"[+] Retrieval Quality Gate finished. Verdict: {report.overall_verdict}")
    print(f"    Summary: {report.summary}")
    print(f"    Report saved to {out_p}")


if __name__ == "__main__":
    main()
