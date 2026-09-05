"""cognitive_core/benchmarks/adapter_performance_benchmark.py — P3-A.7 Adapter Performance Benchmark.

Directly benchmarks and compares:
1. ProductionRetrievalFacade (Direct)
2. RetrievalIntegrationAdapter + ProductionRetrievalFacade (Through Adapter)

Measures:
- Latency distribution (Mean, Median, P95, P99)
- Absolute adapter overhead (ms)
- Percentage adapter overhead (%)
- Target threshold: overhead < 10%

Emits: 07_EVALUATION/ci_evidence/adapter_performance_report.json
Zero storage mutation.
"""
from __future__ import annotations

import argparse
import json
import math
import sys
import time
from dataclasses import asdict, dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Sequence, Tuple

from ..hybrid_retrieval import HybridRetriever, tokenize
from ..integration_adapter import IntegrationSearchRequest, RetrievalIntegrationAdapter
from ..retrieval_boundary import RetrievalBoundaryAdapter
from ..retrieval_facade import FacadeRetrievalRequest, ProductionRetrievalFacade
from ..vault_index import VaultIndex


def _ensure_utf8_streams() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass


def _calc_stats(values: Sequence[float]) -> Tuple[float, float, float, float]:
    if not values:
        return 0.0, 0.0, 0.0, 0.0
    s = sorted(values)
    mean = sum(s) / len(s)
    med = s[len(s) // 2]
    p95_idx = min(len(s) - 1, max(0, int(math.ceil(0.95 * len(s))) - 1))
    p99_idx = min(len(s) - 1, max(0, int(math.ceil(0.99 * len(s))) - 1))
    return round(mean, 3), round(med, 3), round(s[p95_idx], 3), round(s[p99_idx], 3)


@dataclass
class ArmLatencyStats:
    arm: str
    sample_queries: int
    mean_ms: float
    median_ms: float
    p95_ms: float
    p99_ms: float


@dataclass
class AdapterOverheadComparison:
    absolute_overhead_ms: float
    percentage_overhead: float
    target_threshold: str
    status: str  # PASS or FAIL


@dataclass
class AdapterPerformanceReport:
    timestamp_utc: str
    vault_root: str
    facade_direct: ArmLatencyStats
    adapter_layered: ArmLatencyStats
    overhead: AdapterOverheadComparison
    queries_evaluated: int
    verdict: str


def run_adapter_benchmark(vault_root: Path | str = ".", sample_size: int = 50) -> AdapterPerformanceReport:
    root = Path(vault_root)
    idx = VaultIndex.load(root)
    retriever = HybridRetriever(idx)
    boundary = RetrievalBoundaryAdapter(retriever)
    facade = ProductionRetrievalFacade(adapter=boundary)
    adapter = RetrievalIntegrationAdapter(facade=facade)

    eligible = [
        n for n in idx.notes
        if n.lifecycle == "ACTIVE" and n.verification == "verified" and n.title and len(tokenize(n.text)) >= 10
    ]
    test_notes = eligible[:sample_size]

    # Warmup
    for n in test_notes[:5]:
        facade.retrieve(FacadeRetrievalRequest(query=n.title, principal="human", page_size=10))
        adapter.search(IntegrationSearchRequest(query=n.title, principal="human", page_size=10))

    # Benchmark: Interleaved execution across test queries
    direct_lats: List[float] = []
    adapter_lats: List[float] = []
    for i, n in enumerate(test_notes):
        f_req = FacadeRetrievalRequest(query=n.title, principal="human", page_size=10, request_id=f"facade-{i}")
        a_req = IntegrationSearchRequest(query=n.title, principal="human", page_size=10, request_id=f"adapter-{i}")

        t0 = time.perf_counter()
        facade.retrieve(f_req)
        direct_lats.append((time.perf_counter() - t0) * 1000.0)

        t1 = time.perf_counter()
        adapter.search(a_req)
        adapter_lats.append((time.perf_counter() - t1) * 1000.0)

    f_mean, f_med, f_p95, f_p99 = _calc_stats(direct_lats)
    a_mean, a_med, a_p95, a_p99 = _calc_stats(adapter_lats)

    abs_overhead = round(a_med - f_med, 3)
    # percentage overhead relative to direct median
    pct_overhead = round((abs_overhead / f_med) * 100.0, 2) if f_med > 0 else 0.0

    # In sub-millisecond ranges (e.g. 0.70ms vs 0.73ms), noise can produce ~4-6%, threshold is < 10%
    status = "PASS" if pct_overhead <= 10.0 else "FAIL — OPTIMIZATION REQUIRED"

    facade_stats = ArmLatencyStats("Facade Direct", len(test_notes), f_mean, f_med, f_p95, f_p99)
    adapter_stats = ArmLatencyStats("Adapter + Facade", len(test_notes), a_mean, a_med, a_p95, a_p99)
    overhead_stats = AdapterOverheadComparison(abs_overhead, pct_overhead, "<= 10.0%", status)

    return AdapterPerformanceReport(
        timestamp_utc=datetime.now(timezone.utc).isoformat(),
        vault_root=str(root.resolve()),
        facade_direct=facade_stats,
        adapter_layered=adapter_stats,
        overhead=overhead_stats,
        queries_evaluated=len(test_notes),
        verdict=status,
    )


def main() -> None:
    _ensure_utf8_streams()
    parser = argparse.ArgumentParser(description="P3-A.7 Adapter Performance Benchmark")
    parser.add_argument("--vault-root", default=".", help="Root path of the vault")
    parser.add_argument(
        "--output",
        default="07_EVALUATION/ci_evidence/adapter_performance_report.json",
        help="Destination path for JSON report",
    )
    args = parser.parse_args()

    print("[*] Running P3-A.7 Adapter Performance Benchmark...")
    report = run_adapter_benchmark(args.vault_root)

    out_p = Path(args.output)
    out_p.parent.mkdir(parents=True, exist_ok=True)
    with open(out_p, "w", encoding="utf-8") as f:
        json.dump(asdict(report), f, indent=2, ensure_ascii=False)

    print(f"[+] Benchmark complete. Verdict: {report.verdict}")
    print(f"    Facade Direct : Median={report.facade_direct.median_ms:.3f} ms, P95={report.facade_direct.p95_ms:.3f} ms")
    print(f"    Adapter Layer : Median={report.adapter_layered.median_ms:.3f} ms, P95={report.adapter_layered.p95_ms:.3f} ms")
    print(f"    Overhead      : {report.overhead.absolute_overhead_ms:+.3f} ms ({report.overhead.percentage_overhead:+.2f}%)")


if __name__ == "__main__":
    main()
