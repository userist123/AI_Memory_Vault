"""The ceiling a reranker could reach, measured without the arm contaminating it.

The loss funnel's oracle ceiling is not a property of the candidate pool, though
it is quoted as one. `diagnose_case` resolves `gold_rank` by looking in the
returned page first and only then in the fused candidate ranking, so a gold note
sitting at fused rank 250 is recorded as rank 5 whenever the arm under test
happened to put it on the page. The ceiling therefore moves with the arm: 76.15%
at k=200 under `baseline`, 72.31% under `fused_score`, for the same pool.

A reranker reorders the candidate pool. Its ceiling is "is the gold note in the
pool at all, within the first k of the fusion order" — and nothing about that
depends on which sort key the controller applies afterwards. That is what this
measures: one pass, gold's rank taken only from `candidate_trace.fused_ranking`,
plus whether it is anywhere in the pool regardless of rank.

    python 30_SCRIPTS/evaluation/measure_reranker_ceiling.py
"""
from __future__ import annotations

import hashlib
import json
import math
import os
import sys
from pathlib import Path
from typing import Any, Dict, List, Optional

REPO = Path(__file__).resolve().parents[2]
BENCH_JSON = REPO / "07_EVALUATION" / "retrieval_benchmark_v3" / "retrieval_benchmark_v3.json"
BENCH_SHA = REPO / "07_EVALUATION" / "retrieval_benchmark_v3" / "retrieval_benchmark_v3.json.sha256"
OUT = REPO / "07_EVALUATION" / "ranking_formula" / "reranker_ceiling.json"

sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

from memory_controller.authorizer import Principal  # noqa: E402
from memory_controller.controller import MemoryController  # noqa: E402
from memory_controller.storage.file_engine import FileStorageEngine  # noqa: E402
from retrieval.vault_index import VaultIndex  # noqa: E402

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

K_LEVELS = (5, 10, 20, 50, 100, 200)


def wilson(k: int, n: int) -> Dict[str, float]:
    if n == 0:
        return {"proportion": 0.0, "lower": 0.0, "upper": 0.0}
    p, z = k / n, 1.959963984540054
    d = 1.0 + z * z / n
    centre = (p + z * z / (2.0 * n)) / d
    margin = (z / d) * math.sqrt(p * (1.0 - p) / n + z * z / (4.0 * n * n))
    return {"proportion": round(p, 4), "lower": round(max(0.0, centre - margin), 4),
            "upper": round(min(1.0, centre + margin), 4)}


def fused_rank_of_gold(trace: dict, gold: set[str]) -> Optional[int]:
    """Gold's position in the fusion order, or None if no gold note is in the pool.

    Read only from `fused_ranking`. The returned page is deliberately not
    consulted: that is the step that made the funnel's ceiling arm-dependent.
    """
    best: Optional[int] = None
    for entry in trace.get("fused_ranking") or []:
        if not isinstance(entry, dict) or entry.get("id") not in gold:
            continue
        rank = entry.get("rank")
        if rank is None:
            continue
        best = rank if best is None else min(best, int(rank))
    return best


def main() -> int:
    expected = BENCH_SHA.read_text(encoding="utf-8").split()[0]
    actual = hashlib.sha256(BENCH_JSON.read_bytes()).hexdigest()
    if actual != expected:
        raise SystemExit(f"FROZEN_BENCHMARK_HASH_MISMATCH: {actual} != {expected}")

    cases = [c for c in json.loads(BENCH_JSON.read_text(encoding="utf-8"))["cases"]
             if not c.get("abstain") and c.get("gold_relevant_notes")]

    cwd = os.getcwd()
    os.chdir(REPO)
    try:
        index = VaultIndex.load(Path("."), include_raw=True, include_archived=True)
        storage = FileStorageEngine(".")
    finally:
        os.chdir(cwd)

    controller = MemoryController(storage=storage, index=index, enable_graph_expansion=False)
    rows: List[Dict[str, Any]] = []
    for case in cases:
        pack = controller.search(Principal.AI_AGENT, case["query"], page_size=5)
        trace = pack.get("candidate_trace", {}) or {}
        gold = set(case["gold_relevant_notes"])
        page = [r.get("id") for r in pack.get("results", []) if r.get("id")]
        rows.append({
            "id": case["id"],
            "class": case["class"],
            "fused_rank_of_gold": fused_rank_of_gold(trace, gold),
            "gold_in_pool": any(
                isinstance(e, dict) and e.get("id") in gold
                for e in (trace.get("fused_ranking") or [])),
            "pool_size": len(trace.get("fused_ranking") or []),
            "hit_on_page": bool(gold & set(page)),
        })

    n = len(rows)
    achieved = sum(1 for r in rows if r["hit_on_page"])
    ceiling = {}
    for k in K_LEVELS:
        reach = sum(1 for r in rows
                    if r["fused_rank_of_gold"] is not None and r["fused_rank_of_gold"] <= k)
        ceiling[str(k)] = {"k": k, "reachable_cases": reach,
                           "recall": round(reach / n, 4), "ci_95": wilson(reach, n)}
    in_pool = sum(1 for r in rows if r["gold_in_pool"])

    payload = {
        "schema": "reranker-ceiling.v1",
        "benchmark_sha256": actual,
        "operating_point": {"principal": "AI_AGENT", "page_size": 5,
                            "agent_lifecycle_floor": True, "graph_expansion": False},
        "ranking_arm": "production default (fused_score); irrelevant to the ceiling by construction",
        "n_cases": n,
        "achieved_recall": {"k": achieved, "n": n, "text": f"{achieved}/{n}",
                            "ci_95": wilson(achieved, n)},
        "gold_anywhere_in_pool": {"k": in_pool, "n": n, "text": f"{in_pool}/{n}",
                                  "ci_95": wilson(in_pool, n)},
        "ceiling_by_k": ceiling,
        "mean_pool_size": round(sum(r["pool_size"] for r in rows) / n, 2),
        "note": ("Gold's rank is read only from candidate_trace.fused_ranking, never "
                 "from the returned page. The funnel reads the page first, which is "
                 "why its ceiling differs between arms for an identical pool."),
        "rows": rows,
    }
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
                   encoding="utf-8", newline="\n")

    print(f"CASES={n} MEAN_POOL={payload['mean_pool_size']}")
    print(f"ACHIEVED={achieved}/{n}")
    print(f"GOLD_IN_POOL={in_pool}/{n}")
    for k, v in ceiling.items():
        print(f"CEILING@{k:>3}={v['reachable_cases']}/{n} ({v['recall'] * 100:.2f}%)")
    print(f"written {OUT.relative_to(REPO)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
