"""Research-only paired H1 associative experiment.

Compares the frozen production baseline (graph expansion OFF) with the
existing optional graph-expansion path (graph ON). No production files are
modified. The frozen corpus/benchmark identities below are an admission gate.
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "03_IMPLEMENTATION" / "packages"))
os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)
os.environ["ANTIGRAVITY_ARTIFACT_DIR"] = str(REPO_ROOT / "artifacts" / "h1")

from memory_controller.authorizer import Principal
from memory_controller.controller import MemoryController, RANKING_ARM_FUSED_SCORE, GraphExpansionDegraded
from retrieval.vault_index import VaultIndex
from memory_controller.storage.file_engine import FileStorageEngine
from lifecycle.validation.book_to_memory_run_config import RunConfig, compare_results, stamp

CASES_PATH = REPO_ROOT / "artifacts" / "h1" / "h1_cases.json"
CORPUS_PATH = REPO_ROOT / "artifacts" / "h1" / "current_labeling_corpus.json"
RESULTS_PATH = REPO_ROOT / "artifacts" / "h1" / "h1_associative_experiment_results.json"

FROZEN_CORPUS_HASH = "bd6eabcfd6ba3a1292365f078705fdb6a3b036e81dca8bdff4e58d9de654e398"
FROZEN_BENCHMARK_HASH = "59bc81e42c0f703bdfb7e966df75a496590669bbeb82c5eafa2d18aef7c3a37f"
FROZEN_CORPUS_COMMIT = "5d2d36640b7dcb37ed70c7f96a20607bc6d895f0"


VARIABLES_UNDER_TEST = ["controls.enable_graph_expansion", "controls.strict_graph_expansion"]


def arm_run_config(graph_on: bool, reps: int) -> RunConfig:
    """Explicit run config of one arm (PR #209 B08). The two arms differ only in the graph flags."""
    return RunConfig.for_retrieval({
        "principal": "HUMAN",
        "page_size": 10,
        "ranking_arm": "fused_score",
        "lifecycle_and_authorization": "unchanged",
        "token_budget": "unchanged",
        "repetitions": reps,
        "enable_graph_expansion": graph_on,
        "strict_graph_expansion": graph_on,
    })


def guarded_arm_configs(reps: int) -> Dict[str, Any]:
    """Build both arm configs and refuse the comparison unless only the graph flags differ."""
    base, var = arm_run_config(False, reps), arm_run_config(True, reps)
    guard = compare_results(stamp({}, base), stamp({}, var), VARIABLES_UNDER_TEST)
    return {"baseline": base, "variant": var, "guard": guard}


def load_frozen() -> List[Dict[str, Any]]:
    cases = json.loads(CASES_PATH.read_text(encoding="utf-8"))
    json.loads(CORPUS_PATH.read_text(encoding="utf-8"))
    if cases.get("corpus_hash") != FROZEN_CORPUS_HASH:
        raise RuntimeError("frozen corpus hash mismatch")
    if cases.get("benchmark_hash") != FROZEN_BENCHMARK_HASH:
        raise RuntimeError("frozen benchmark hash mismatch")
    if cases.get("corpus_commit") != FROZEN_CORPUS_COMMIT:
        raise RuntimeError("frozen corpus commit mismatch")
    if any(c.get("split") not in {"development", "calibration", "held_out"} for c in cases["cases"]):
        raise RuntimeError("invalid split in frozen case set")
    return cases["cases"]


def run_arm(controller: MemoryController, cases: List[Dict[str, Any]], reps: int) -> List[Dict[str, Any]]:
    rows = []
    for case in cases:
        rep_ids: List[List[str]] = []
        latencies: List[float] = []
        graph_traces: List[Dict[str, Any]] = []
        for _ in range(reps):
            t0 = time.perf_counter()
            pack = controller.search(Principal.HUMAN, case["query"], page_size=10)
            latencies.append(round((time.perf_counter() - t0) * 1000.0, 2))
            rep_ids.append([x.get("id") for x in pack.get("results", []) if x.get("id")])
            graph_traces.append((pack.get("candidate_trace") or {}).get("graph_expansion") or {})
        gold = set(case.get("gold_relevant_notes", []))
        hits10 = [bool(gold.intersection(set(ids[:10]))) for ids in rep_ids]
        all_gold10 = [gold.issubset(set(ids[:10])) for ids in rep_ids]
        deterministic = all(ids == rep_ids[0] for ids in rep_ids)
        rows.append({
            "id": case["id"],
            "family": case["family"],
            "split": case["split"],
            "gold": sorted(gold),
            "hits_at_10": hits10,
            "all_gold_at_10": all_gold10,
            "mean_latency_ms": round(sum(latencies) / len(latencies), 2),
            "latencies_ms": latencies,
            "deterministic": deterministic,
            "retrieved_ids": rep_ids[0],
            "graph_traces": graph_traces,
        })
    return rows


def aggregate(rows: List[Dict[str, Any]], family: str | None = None, split: str | None = None) -> Dict[str, Any]:
    selected = [r for r in rows if (family is None or r["family"] == family) and (split is None or r["split"] == split)]
    if not selected:
        return {"cases": 0, "recall_at_10": None, "all_gold_recall_at_10": None}
    return {
        "cases": len(selected),
        "recall_at_10": round(sum(r["hits_at_10"][0] for r in selected) / len(selected), 4),
        "all_gold_recall_at_10": round(sum(r["all_gold_at_10"][0] for r in selected) / len(selected), 4),
        "mean_latency_ms": round(sum(r["mean_latency_ms"] for r in selected) / len(selected), 2),
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--reps", type=int, default=3)
    args = parser.parse_args()
    if args.reps < 2:
        raise SystemExit("--reps must be >= 2 for repeatability evidence")

    cases = load_frozen()
    arms = guarded_arm_configs(args.reps)
    index = VaultIndex.load(REPO_ROOT, include_raw=True, include_archived=True)
    storage = FileStorageEngine(str(REPO_ROOT))

    baseline = MemoryController(storage=storage, index=index,
                                ranking_arm=RANKING_ARM_FUSED_SCORE,
                                enable_graph_expansion=False,
                                strict_graph_expansion=False)
    variant = MemoryController(storage=storage, index=index,
                               ranking_arm=RANKING_ARM_FUSED_SCORE,
                               enable_graph_expansion=True,
                               strict_graph_expansion=True)

    baseline_rows = run_arm(baseline, cases, args.reps)
    try:
        variant_rows = run_arm(variant, cases, args.reps)
    except GraphExpansionDegraded as exc:
        result = {
            "experiment_id": "H1-BOOK-002",
            "variant_id": "graph_1hop_typed_strict",
            "status": "BLOCKED",
            "run_id": datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ"),
            "frozen": {
                "corpus_commit": FROZEN_CORPUS_COMMIT,
                "corpus_hash": FROZEN_CORPUS_HASH,
                "benchmark_hash": FROZEN_BENCHMARK_HASH,
            },
            "repetitions": args.reps,
            "controls": {
                "principal": "HUMAN",
                "page_size": 10,
                "ranking_arm": "fused_score",
                "lifecycle_and_authorization": "unchanged",
                "token_budget": "unchanged",
                "case_set": "identical paired cases",
            },
            "block_reason": str(exc),
            "variant_run_config": arms["variant"].to_dict(),
            "comparison_guard": arms["guard"],
            "baseline": baseline_rows,
            "variant": None,
            "scientific_disposition": (
                "No paired variant result is reported. The strict graph guard "
                "correctly detected that graph expansion produced no new nodes; "
                "treating this as a zero-effect result would be invalid."
            ),
        }
        stamp(result, arms["baseline"])
        RESULTS_PATH.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
        print(json.dumps({
            "status": result["status"],
            "block_reason": result["block_reason"],
            "scientific_disposition": result["scientific_disposition"],
        }, indent=2))
        print(f"saved {RESULTS_PATH}")
        return 0

    primary_b = aggregate(baseline_rows, family="indirect_cue", split="held_out")
    primary_v = aggregate(variant_rows, family="indirect_cue", split="held_out")
    protected_b = aggregate(baseline_rows, split="held_out")
    protected_v = aggregate(variant_rows, split="held_out")

    result = {
        "experiment_id": "H1-BOOK-002",
        "variant_id": "graph_1hop_typed_strict",
        "run_id": datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ"),
        "frozen": {
            "corpus_commit": FROZEN_CORPUS_COMMIT,
            "corpus_hash": FROZEN_CORPUS_HASH,
            "benchmark_hash": FROZEN_BENCHMARK_HASH,
        },
        "repetitions": args.reps,
        "controls": {
            "principal": "HUMAN",
            "page_size": 10,
            "ranking_arm": "fused_score",
            "lifecycle_and_authorization": "unchanged",
            "token_budget": "unchanged",
            "case_set": "identical paired cases",
        },
        "primary": {
            "baseline": primary_b,
            "variant": primary_v,
            "absolute_delta_recall_at_10": None if primary_b["recall_at_10"] is None else round(primary_v["recall_at_10"] - primary_b["recall_at_10"], 4),
        },
        "protected_held_out": {
            "baseline": protected_b,
            "variant": protected_v,
            "absolute_delta_recall_at_10": None if protected_b["recall_at_10"] is None else round(protected_v["recall_at_10"] - protected_b["recall_at_10"], 4),
        },
        "variant_run_config": arms["variant"].to_dict(),
        "comparison_guard": arms["guard"],
        "baseline": baseline_rows,
        "variant": variant_rows,
    }
    stamp(result, arms["baseline"])
    RESULTS_PATH.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(result["primary"], indent=2))
    print(json.dumps(result["protected_held_out"], indent=2))
    print(f"saved {RESULTS_PATH}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
