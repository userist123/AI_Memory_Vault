"""LoCoMo-style internal retrieval benchmark harness.

Evaluates any retrieval function against a fixed set of (query, relevant_ids)
cases using precision@k, recall@k, and MRR. Does not call external services
and never mutates canonical memory.
"""
from __future__ import annotations

from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Callable, Dict, List, Sequence
import json

from .metrics import mean_reciprocal_rank, precision_at_k, recall_at_k


@dataclass
class BenchmarkCase:
    query: str
    relevant_ids: List[str]
    description: str = ""


@dataclass
class BenchmarkResult:
    case: str
    precision_at_5: float
    recall_at_5: float
    mrr: float
    retrieved_top5: List[str] = field(default_factory=list)


class RetrievalBenchmark:
    def __init__(self, cases: List[BenchmarkCase]):
        self.cases = cases

    @classmethod
    def load_jsonl(cls, path) -> "RetrievalBenchmark":
        cases: List[BenchmarkCase] = []
        for line in Path(path).read_text(encoding="utf-8").splitlines():
            if line.strip():
                data = json.loads(line)
                cases.append(BenchmarkCase(**data))
        return cls(cases)

    def run(self, retrieval_fn: Callable[[str], Sequence[str]], k: int = 5) -> Dict[str, object]:
        results: List[BenchmarkResult] = []
        for case in self.cases:
            retrieved = list(retrieval_fn(case.query))
            results.append(BenchmarkResult(
                case=case.query,
                precision_at_5=round(precision_at_k(retrieved, case.relevant_ids, k), 4),
                recall_at_5=round(recall_at_k(retrieved, case.relevant_ids, k), 4),
                mrr=round(mean_reciprocal_rank(retrieved, case.relevant_ids), 4),
                retrieved_top5=retrieved[:k],
            ))
        if results:
            avg_precision = sum(r.precision_at_5 for r in results) / len(results)
            avg_recall = sum(r.recall_at_5 for r in results) / len(results)
            avg_mrr = sum(r.mrr for r in results) / len(results)
        else:
            avg_precision = avg_recall = avg_mrr = 0.0
        return {
            "results": [asdict(r) for r in results],
            "summary": {
                "cases": len(results),
                "avg_precision_at_5": round(avg_precision, 4),
                "avg_recall_at_5": round(avg_recall, 4),
                "avg_mrr": round(avg_mrr, 4),
            },
        }
