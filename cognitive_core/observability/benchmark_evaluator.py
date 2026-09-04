import json
import time
from dataclasses import dataclass, field, asdict
from typing import List, Dict, Any, Optional, Set
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal
from cognitive_core.observability.retrieval_tracer import RetrievalTracer, RetrievalTrace

@dataclass
class QueryBenchmarkCase:
    case_id: str
    query: str
    archetype: str
    target_ids: List[str]
    hard_negative_ids: List[str] = field(default_factory=list)
    should_abstain: bool = False

@dataclass
class QueryEvaluationResult:
    case_id: str
    query: str
    archetype: str
    target_ids: List[str]
    retrieved_ids: List[str]
    abstained: bool
    should_abstain: bool
    abstention_correct: bool
    precision_at_1: float
    precision_at_3: float
    precision_at_5: float
    recall_at_1: float
    recall_at_3: float
    recall_at_5: float
    reciprocal_rank: float
    false_positive: bool
    top_score: float

@dataclass
class BenchmarkSummary:
    total_queries: int
    mean_precision_at_1: float
    mean_precision_at_3: float
    mean_precision_at_5: float
    mean_recall_at_1: float
    mean_recall_at_3: float
    mean_recall_at_5: float
    mean_reciprocal_rank: float
    abstention_accuracy: float
    false_positive_rate: float
    archetype_breakdown: Dict[str, Dict[str, float]]
    results: List[QueryEvaluationResult] = field(default_factory=list)

    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)


class RetrievalBenchmarkEvaluator:
    """
    Antigravity Retrieval Metrics & Benchmark Evaluator (R001).
    Measures Precision@K, Recall@K, MRR, False Positive Rate, and Abstention Accuracy
    across held-out query archetypes.
    """
    def __init__(self, tracer: Optional[RetrievalTracer] = None):
        self.tracer = tracer or RetrievalTracer()

    def evaluate_case(self,
                      case: QueryBenchmarkCase,
                      controller: MemoryController,
                      principal: Principal = Principal.AI_AGENT,
                      abstention_threshold: float = 0.20,
                      top_k: int = 5) -> QueryEvaluationResult:
        trace = self.tracer.trace(
            query=case.query,
            controller=controller,
            principal=principal,
            abstention_threshold=abstention_threshold,
            page_size=top_k
        )

        retrieved = trace.admitted_note_ids
        targets = set(case.target_ids)
        abstained = trace.abstained

        # Abstention accuracy
        abstention_correct = (abstained == case.should_abstain)

        if abstained:
            # If abstained, zero items admitted
            return QueryEvaluationResult(
                case_id=case.case_id,
                query=case.query,
                archetype=case.archetype,
                target_ids=case.target_ids,
                retrieved_ids=[],
                abstained=True,
                should_abstain=case.should_abstain,
                abstention_correct=abstention_correct,
                precision_at_1=0.0,
                precision_at_3=0.0,
                precision_at_5=0.0,
                recall_at_1=0.0,
                recall_at_3=0.0,
                recall_at_5=0.0,
                reciprocal_rank=0.0,
                false_positive=False,
                top_score=trace.best_score
            )

        # Precision & Recall at K
        def p_at_k(k: int) -> float:
            sub = retrieved[:k]
            if not sub:
                return 0.0
            hits = len([x for x in sub if x in targets])
            return hits / float(k)

        def r_at_k(k: int) -> float:
            if not targets:
                return 1.0 if not retrieved[:k] else 0.0
            sub = retrieved[:k]
            hits = len([x for x in sub if x in targets])
            return hits / float(len(targets))

        # Reciprocal Rank (RR)
        rr = 0.0
        for i, item in enumerate(retrieved, 1):
            if item in targets:
                rr = 1.0 / float(i)
                break

        # False positive check: if query was supposed to abstain or had no targets, but returned results
        is_fp = False
        if case.should_abstain and len(retrieved) > 0:
            is_fp = True
        elif case.hard_negative_ids:
            # Check if hard negatives were surfaced
            if any(hn in retrieved for hn in case.hard_negative_ids):
                is_fp = True

        return QueryEvaluationResult(
            case_id=case.case_id,
            query=case.query,
            archetype=case.archetype,
            target_ids=case.target_ids,
            retrieved_ids=retrieved,
            abstained=False,
            should_abstain=case.should_abstain,
            abstention_correct=abstention_correct,
            precision_at_1=p_at_k(1),
            precision_at_3=p_at_k(3),
            precision_at_5=p_at_k(5),
            recall_at_1=r_at_k(1),
            recall_at_3=r_at_k(3),
            recall_at_5=r_at_k(5),
            reciprocal_rank=rr,
            false_positive=is_fp,
            top_score=trace.best_score
        )

    def evaluate_suite(self,
                       cases: List[QueryBenchmarkCase],
                       controller: MemoryController,
                       principal: Principal = Principal.AI_AGENT,
                       abstention_threshold: float = 0.20) -> BenchmarkSummary:
        results = []
        for case in cases:
            res = self.evaluate_case(case, controller, principal=principal, abstention_threshold=abstention_threshold)
            results.append(res)

        n = len(results)
        if n == 0:
            return BenchmarkSummary(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, {})

        # Compute aggregate metrics
        m_p1 = sum(r.precision_at_1 for r in results) / n
        m_p3 = sum(r.precision_at_3 for r in results) / n
        m_p5 = sum(r.precision_at_5 for r in results) / n
        m_r1 = sum(r.recall_at_1 for r in results) / n
        m_r3 = sum(r.recall_at_3 for r in results) / n
        m_r5 = sum(r.recall_at_5 for r in results) / n
        m_mrr = sum(r.reciprocal_rank for r in results) / n
        m_abs_acc = sum(1.0 for r in results if r.abstention_correct) / n
        m_fpr = sum(1.0 for r in results if r.false_positive) / n

        # Archetype breakdown
        by_arch: Dict[str, List[QueryEvaluationResult]] = {}
        for r in results:
            by_arch.setdefault(r.archetype, []).append(r)

        arch_breakdown = {}
        for arch, arch_res in by_arch.items():
            an = len(arch_res)
            arch_breakdown[arch] = {
                "count": an,
                "p@1": sum(r.precision_at_1 for r in arch_res) / an,
                "p@3": sum(r.precision_at_3 for r in arch_res) / an,
                "r@1": sum(r.recall_at_1 for r in arch_res) / an,
                "r@3": sum(r.recall_at_3 for r in arch_res) / an,
                "mrr": sum(r.reciprocal_rank for r in arch_res) / an,
                "abstention_acc": sum(1.0 for r in arch_res if r.abstention_correct) / an,
                "false_positives": sum(1.0 for r in arch_res if r.false_positive) / an
            }

        return BenchmarkSummary(
            total_queries=n,
            mean_precision_at_1=m_p1,
            mean_precision_at_3=m_p3,
            mean_precision_at_5=m_p5,
            mean_recall_at_1=m_r1,
            mean_recall_at_3=m_r3,
            mean_recall_at_5=m_r5,
            mean_reciprocal_rank=m_mrr,
            abstention_accuracy=m_abs_acc,
            false_positive_rate=m_fpr,
            archetype_breakdown=arch_breakdown,
            results=results
        )
