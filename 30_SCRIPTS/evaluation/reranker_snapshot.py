"""Reranker experiment, phase 1: production-path candidate snapshot (no reranker).

Runs every benchmark v3 query once through the production controller
(``controller_factory.get_memory_controller()``, ``Principal.AI_AGENT``,
``page_size=5``, exactly as ``interfaces/memory_access.search``) and stores:

* the baseline top 5 (``pack["results"]``, not ``fused_ranking[:5]``);
* the fused candidate ranking (ids, ranks, scores; up to 200);
* the trace fields that make a run valid (``source`` must be ``generated``,
  ``ranking_arm`` must be the production default);
* the reranker input text for every candidate note that appears in any top 50:
  title + first 1200 characters of the same ``content`` BM25 indexes.

The snapshot is the single input of every reranker arm (phase 2), so all arms
see the same candidates. Gold labels are read only to compute the baseline and
the oracle@K ceiling; nothing here depends on any reranker output.

Usage:
    python 30_SCRIPTS/evaluation/reranker_snapshot.py [--out DIR]
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys
import time
from pathlib import Path
from typing import Any, Dict, List

REPO_ROOT = Path(__file__).resolve().parents[2]
for p in (REPO_ROOT, REPO_ROOT / "03_IMPLEMENTATION" / "packages", REPO_ROOT / "03_IMPLEMENTATION"):
    if str(p) not in sys.path:
        sys.path.insert(0, str(p))

os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)
os.environ["MEMORY_VAULT_ROOT"] = str(REPO_ROOT)

from memory_controller.authorizer import Principal  # noqa: E402
from memory_controller.controller_factory import get_memory_controller  # noqa: E402

BENCH = REPO_ROOT / "07_EVALUATION" / "retrieval_benchmark_v3" / "retrieval_benchmark_v3.json"
BENCH_SHA = BENCH.with_suffix(".json.sha256")
DEFAULT_OUT = REPO_ROOT / "07_EVALUATION" / "reranker_experiment"
TEXT_CHARS = 1200
SNAPSHOT_TOP = 50
ORACLE_KS = (5, 10, 20, 30, 50, 100, 200)


def note_title(note: Dict[str, Any]) -> str:
    title = note.get("title")
    if isinstance(title, str) and title.strip():
        return title.strip()
    content = note.get("content")
    if isinstance(content, str):
        for line in content.splitlines():
            if line.startswith("# "):
                return line[2:].strip()
    return str(note.get("category") or note.get("id") or "untitled")


def rerank_text(note: Dict[str, Any]) -> str:
    content = note.get("content") or ""
    if not isinstance(content, str):
        content = str(content)
    return f"{note_title(note)}\n{content[:TEXT_CHARS]}"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT)
    args = ap.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)

    expected = BENCH_SHA.read_text(encoding="utf-8").split()[0]
    actual = hashlib.sha256(BENCH.read_bytes()).hexdigest()
    if actual != expected:
        raise SystemExit(f"FROZEN_BENCHMARK_HASH_MISMATCH {actual} != {expected}")
    cases = json.loads(BENCH.read_text(encoding="utf-8"))["cases"]

    controller = get_memory_controller()
    storage = controller.storage
    rows: List[Dict[str, Any]] = []
    texts: Dict[str, str] = {}
    for case in cases:
        controller.cache.clear() if hasattr(controller.cache, "clear") else None
        t0 = time.perf_counter()
        pack = controller.search(Principal.AI_AGENT, case["query"], page_size=5)
        ms = (time.perf_counter() - t0) * 1000.0
        trace = pack.get("candidate_trace") or {}
        fused = [
            {"id": e.get("id"), "rank": e.get("rank"), "fused_score": e.get("fused_score")}
            for e in (trace.get("fused_ranking") or []) if isinstance(e, dict)
        ]
        baseline = [r.get("id") for r in (pack.get("results") or [])[:5]]
        for e in fused[:SNAPSHOT_TOP]:
            nid = e["id"]
            if nid and nid not in texts:
                note = storage.get(nid)
                if note:
                    texts[nid] = rerank_text(note)
        gold = set(case.get("gold_relevant_notes") or [])
        fused_ids = [e["id"] for e in fused]
        gold_fused_rank = next((i for i, nid in enumerate(fused_ids, 1) if nid in gold), None)
        rows.append({
            "id": case["id"], "class": case["class"], "language": case.get("language"),
            "abstain": bool(case.get("abstain")), "query": case["query"],
            "gold": sorted(gold),
            "source": trace.get("source"), "ranking_arm": trace.get("ranking_arm"),
            "classifier_filter_arm": trace.get("classifier_filter_arm"),
            "baseline_top5": baseline,
            "baseline_hit5": bool(gold & set(baseline)),
            "gold_fused_rank": gold_fused_rank,
            "fused_top200": fused[:200],
            "search_ms": round(ms, 1),
        })

    measurable = [r for r in rows if not r["abstain"]]
    summary = {
        "benchmark_sha256": actual,
        "git_head": os.popen(f'git -C "{REPO_ROOT}" rev-parse HEAD').read().strip(),
        "storage_engine": type(storage).__name__,
        "cases": len(rows), "measurable": len(measurable),
        "sources": sorted({str(r["source"]) for r in rows}),
        "ranking_arms": sorted({str(r["ranking_arm"]) for r in rows}),
        "classifier_filter_arms": sorted({str(r["classifier_filter_arm"]) for r in rows}),
        "baseline_hit5": sum(r["baseline_hit5"] for r in measurable),
        "oracle_at_k": {k: sum(1 for r in measurable if r["gold_fused_rank"] and r["gold_fused_rank"] <= k)
                         for k in ORACLE_KS},
        "never_in_fused": sum(1 for r in measurable if r["gold_fused_rank"] is None),
        "unique_texts": len(texts),
        "search_ms_p50": sorted(r["search_ms"] for r in rows)[len(rows) // 2],
    }
    (args.out / "snapshot.json").write_text(json.dumps({"summary": summary, "rows": rows}, ensure_ascii=False, indent=1), encoding="utf-8")
    (args.out / "texts.json").write_text(json.dumps(texts, ensure_ascii=False), encoding="utf-8")
    print(json.dumps(summary, indent=1))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
