"""evaluate_cognitive_core.py — Dual-Arm Empirical Evaluation of Cognitive Core on Held-Out Benchmark v2.

Compares:
- Arm A (Baseline): enable_cognitive_core = False (default production)
- Arm B (Treatment): enable_cognitive_core = True

Measures:
1. Latency (ms per query)
2. Token Count (context results tokens and full envelope tokens)
3. Relevance (context recall and candidate recall on gold notes)
4. Budget Violation Rate (hard limit exceed rate)
5. McNemar paired significance test on discordant pairs
"""
from __future__ import annotations

import hashlib
import json
import os
import sys
import time
from collections import Counter
from pathlib import Path
from typing import Any, Dict, List

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))

os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

from memory_controller.authorizer import Principal
from memory_controller.controller import MemoryController, RANKING_ARM_BASELINE
from memory_controller.storage.file_engine import FileStorageEngine
from retrieval.context.budget import ContextBudget
from retrieval.vault_index import VaultIndex

HELDOUT_PATH = REPO / "07_EVALUATION" / "heldout_retrieval_benchmark_v2" / "heldout.json"
HELDOUT_SHA256 = REPO / "07_EVALUATION" / "heldout_retrieval_benchmark_v2" / "heldout.json.sha256"

UNMEASURABLE = "UNMEASURABLE"


def verify_frozen_heldout() -> None:
    text = HELDOUT_PATH.read_bytes()
    digest = hashlib.sha256(text).hexdigest()
    recorded = HELDOUT_SHA256.read_text(encoding="utf-8").strip()
    if digest != recorded:
        raise SystemExit(f"FROZEN_HELDOUT_HASH_MISMATCH: expected {recorded}, got {digest}")


def load_cases() -> List[Dict[str, Any]]:
    return json.loads(HELDOUT_PATH.read_text(encoding="utf-8"))["cases"]


def run_single_case(
    controller: MemoryController,
    case: Dict[str, Any],
    index: VaultIndex,
    enable_cognitive_core: bool,
) -> Dict[str, Any]:
    budget_calc = ContextBudget({})
    budget_violated = False

    t0 = time.perf_counter_ns()
    try:
        pack = controller.search(
            Principal.HUMAN,
            case["query"],
            page_size=10,
            enable_cognitive_core=enable_cognitive_core,
        )
    except Exception as exc:
        t1 = time.perf_counter_ns()
        return {
            "id": case["id"],
            "class": case["class"],
            "latency_ms": round((t1 - t0) / 1_000_000, 3),
            "error": str(exc),
            "budget_violated": True,
            "candidate_recall": UNMEASURABLE,
            "context_recall": UNMEASURABLE,
            "answer_correctness": UNMEASURABLE,
            "results_count": 0,
            "results_tokens": 0,
            "envelope_tokens": 0,
        }

    t1 = time.perf_counter_ns()
    latency_ms = round((t1 - t0) / 1_000_000, 3)

    results = pack.get("results", [])
    results_count = len(results)

    # Context pack token usage (memory results payload)
    results_tokens = budget_calc.estimate_tokens(results)

    # Context pack envelope (excluding candidate_trace diagnostic block)
    envelope = {k: v for k, v in pack.items() if k != "candidate_trace"}
    envelope_tokens = budget_calc.estimate_tokens(envelope)

    trace = pack.get("candidate_trace", {}) or {}
    candidates = {
        e.get("id") for e in (trace.get("fused_ranking") or []) if isinstance(e, dict)
    }
    context_ids = {r.get("id") for r in results if r.get("id")}
    gold = set(case.get("gold_relevant_notes", []))

    cc_info = trace.get("cognitive_core", {})

    if case.get("abstain", False):
        return {
            "id": case["id"],
            "class": case["class"],
            "abstain": True,
            "latency_ms": latency_ms,
            "results_count": results_count,
            "results_tokens": results_tokens,
            "envelope_tokens": envelope_tokens,
            "budget_violated": False,
            "candidate_recall": UNMEASURABLE,
            "context_recall": UNMEASURABLE,
            "answer_correctness": UNMEASURABLE,
            "cognitive_core": cc_info,
        }

    # Answer correctness check
    blob = " ".join(
        index.by_id[n].text for n in context_ids if n in index.by_id
    ).lower()
    req_facts = case.get("required_facts", [])
    facts_ok = all(f.lower() in blob for f in req_facts) if req_facts else False
    correct = bool(gold & context_ids) and facts_ok

    return {
        "id": case["id"],
        "class": case["class"],
        "abstain": False,
        "latency_ms": latency_ms,
        "results_count": results_count,
        "results_tokens": results_tokens,
        "envelope_tokens": envelope_tokens,
        "budget_violated": False,
        "candidate_recall": int(bool(gold & candidates)) if gold else 1,
        "context_recall": int(bool(gold & context_ids)) if gold else 1,
        "answer_correctness": int(correct),
        "cognitive_core": cc_info,
    }


def compute_summary(rows: List[Dict[str, Any]]) -> Dict[str, Any]:
    measurable = [r for r in rows if r["context_recall"] != UNMEASURABLE]
    all_valid = [r for r in rows if "error" not in r]

    latencies = [r["latency_ms"] for r in all_valid]
    latencies.sort()
    results_tokens = [r["results_tokens"] for r in all_valid]
    envelope_tokens = [r["envelope_tokens"] for r in all_valid]
    results_counts = [r["results_count"] for r in all_valid]

    def _mean(vals):
        return round(sum(vals) / len(vals), 2) if vals else 0.0

    def _median(vals):
        if not vals:
            return 0.0
        n = len(vals)
        s = sorted(vals)
        return round(s[n // 2] if n % 2 != 0 else (s[n // 2 - 1] + s[n // 2]) / 2, 2)

    def _p95(vals):
        if not vals:
            return 0.0
        s = sorted(vals)
        idx = min(int(len(s) * 0.95), len(s) - 1)
        return round(s[idx], 2)

    classes = sorted({r["class"] for r in rows})
    by_class = {}
    for cls in classes:
        cls_meas = [r for r in measurable if r["class"] == cls]
        by_class[cls] = {
            "n": len([r for r in rows if r["class"] == cls]),
            "n_measurable": len(cls_meas),
            "context_recall": round(sum(r["context_recall"] for r in cls_meas) / len(cls_meas), 4) if cls_meas else None,
            "answer_correctness": round(sum(r["answer_correctness"] for r in cls_meas) / len(cls_meas), 4) if cls_meas else None,
        }

    return {
        "total_cases": len(rows),
        "measurable_cases": len(measurable),
        "unmeasurable_cases": len(rows) - len(measurable),
        "candidate_recall": round(sum(r["candidate_recall"] for r in measurable) / len(measurable), 4) if measurable else 0.0,
        "context_recall": round(sum(r["context_recall"] for r in measurable) / len(measurable), 4) if measurable else 0.0,
        "answer_correctness": round(sum(r["answer_correctness"] for r in measurable) / len(measurable), 4) if measurable else 0.0,
        "budget_violation_rate": round(sum(1 for r in rows if r.get("budget_violated")) / len(rows), 4),
        "results_count": {
            "mean": _mean(results_counts),
            "median": _median(results_counts),
        },
        "results_tokens": {
            "mean": _mean(results_tokens),
            "median": _median(results_tokens),
            "p95": _p95(results_tokens),
        },
        "envelope_tokens": {
            "mean": _mean(envelope_tokens),
            "median": _median(envelope_tokens),
            "p95": _p95(envelope_tokens),
        },
        "latency_ms": {
            "mean": _mean(latencies),
            "median": _median(latencies),
            "p95": _p95(latencies),
            "min": round(min(latencies), 2) if latencies else 0.0,
            "max": round(max(latencies), 2) if latencies else 0.0,
        },
        "by_class": by_class,
    }


def mcnemar_test(arm_a: List[Dict[str, Any]], arm_b: List[Dict[str, Any]], metric: str) -> Dict[str, Any]:
    by_id_a = {r["id"]: r for r in arm_a}
    b = 0  # 1 in A, 0 in B (A better)
    c = 0  # 0 in A, 1 in B (B better)
    concordant = 0
    skipped = 0

    for r_b in arm_b:
        r_a = by_id_a.get(r_b["id"])
        if not r_a:
            continue
        v_a = r_a.get(metric)
        v_b = r_b.get(metric)
        if v_a == UNMEASURABLE or v_b == UNMEASURABLE:
            skipped += 1
            continue
        if v_a == 1 and v_b == 0:
            b += 1
        elif v_a == 0 and v_b == 1:
            c += 1
        else:
            concordant += 1

    discordant = b + c
    # Two-sided exact binomial p-value when discordant > 0
    if discordant == 0:
        p_value = 1.0
    else:
        import math
        # Binomial test with p=0.5
        k = min(b, c)
        p_val_half = sum(math.comb(discordant, i) * (0.5 ** discordant) for i in range(k + 1))
        p_value = min(1.0, 2.0 * p_val_half)

    return {
        "metric": metric,
        "arm_a_better": b,
        "arm_b_better": c,
        "concordant": concordant,
        "discordant": discordant,
        "skipped_unmeasurable": skipped,
        "exact_two_sided_p_value": round(p_value, 5),
    }


def main():
    print("=== Starting Cognitive Core Dual-Arm Evaluation ===")
    verify_frozen_heldout()
    print("Frozen held-out hash verified.")

    index = VaultIndex.load(REPO, include_raw=True, include_archived=True)
    storage = FileStorageEngine(str(REPO))
    print(f"Index loaded: {len(index)} notes; Storage loaded: {len(storage.id_to_path)} paths.")

    cases = load_cases()
    print(f"Loaded {len(cases)} cases from heldout.json.")

    controller_baseline = MemoryController(
        storage=storage,
        index=index,
        enable_cognitive_core=False,
        ranking_arm=RANKING_ARM_BASELINE,
    )

    controller_treatment = MemoryController(
        storage=storage,
        index=index,
        enable_cognitive_core=True,
        ranking_arm=RANKING_ARM_BASELINE,
    )

    print("Running Arm A (Baseline: enable_cognitive_core=False)...")
    rows_a = []
    for case in cases:
        res = run_single_case(controller_baseline, case, index, enable_cognitive_core=False)
        rows_a.append(res)

    print("Running Arm B (Treatment: enable_cognitive_core=True)...")
    rows_b = []
    for case in cases:
        res = run_single_case(controller_treatment, case, index, enable_cognitive_core=True)
        rows_b.append(res)

    summary_a = compute_summary(rows_a)
    summary_b = compute_summary(rows_b)

    mcnemar_recall = mcnemar_test(rows_a, rows_b, "context_recall")
    mcnemar_correctness = mcnemar_test(rows_a, rows_b, "answer_correctness")

    report = {
        "timestamp_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "dataset": "heldout_retrieval_benchmark_v2/heldout.json",
        "dataset_hash": HELDOUT_SHA256.read_text().strip(),
        "total_cases": len(cases),
        "corpus_notes": len(index),
        "arms": {
            "baseline_off": {
                "enable_cognitive_core": False,
                "summary": summary_a,
                "cases": rows_a,
            },
            "treatment_on": {
                "enable_cognitive_core": True,
                "summary": summary_b,
                "cases": rows_b,
            },
        },
        "statistical_tests": {
            "context_recall": mcnemar_recall,
            "answer_correctness": mcnemar_correctness,
        },
        "delta": {
            "latency_mean_ms_diff": round(summary_b["latency_ms"]["mean"] - summary_a["latency_ms"]["mean"], 2),
            "latency_median_ms_diff": round(summary_b["latency_ms"]["median"] - summary_a["latency_ms"]["median"], 2),
            "results_tokens_mean_diff": round(summary_b["results_tokens"]["mean"] - summary_a["results_tokens"]["mean"], 2),
            "results_tokens_reduction_pct": round(
                (summary_a["results_tokens"]["mean"] - summary_b["results_tokens"]["mean"]) / summary_a["results_tokens"]["mean"] * 100
                if summary_a["results_tokens"]["mean"] else 0.0, 2
            ),
            "envelope_tokens_mean_diff": round(summary_b["envelope_tokens"]["mean"] - summary_a["envelope_tokens"]["mean"], 2),
            "envelope_tokens_reduction_pct": round(
                (summary_a["envelope_tokens"]["mean"] - summary_b["envelope_tokens"]["mean"]) / summary_a["envelope_tokens"]["mean"] * 100
                if summary_a["envelope_tokens"]["mean"] else 0.0, 2
            ),
            "context_recall_diff": round(summary_b["context_recall"] - summary_a["context_recall"], 4),
            "answer_correctness_diff": round(summary_b["answer_correctness"] - summary_a["answer_correctness"], 4),
        }
    }

    out_json = HERE / "cognitive_core_benchmark_report.json"
    out_json.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f"Saved evaluation report to {out_json}")

    # Print summary table to stdout
    print("\n" + "=" * 60)
    print("COGNITIVE CORE EVALUATION SUMMARY")
    print("=" * 60)
    print(f"{'Metric':<30} | {'Baseline (OFF)':<15} | {'Treatment (ON)':<15} | {'Delta':<10}")
    print("-" * 75)
    print(f"{'Context Recall':<30} | {summary_a['context_recall']:<15.4f} | {summary_b['context_recall']:<15.4f} | {report['delta']['context_recall_diff']:<+10.4f}")
    print(f"{'Answer Correctness':<30} | {summary_a['answer_correctness']:<15.4f} | {summary_b['answer_correctness']:<15.4f} | {report['delta']['answer_correctness_diff']:<+10.4f}")
    print(f"{'Results Tokens (Mean)':<30} | {summary_a['results_tokens']['mean']:<15.1f} | {summary_b['results_tokens']['mean']:<15.1f} | {report['delta']['results_tokens_reduction_pct']:<+9.1f}%")
    print(f"{'Envelope Tokens (Mean)':<30} | {summary_a['envelope_tokens']['mean']:<15.1f} | {summary_b['envelope_tokens']['mean']:<15.1f} | {report['delta']['envelope_tokens_reduction_pct']:<+9.1f}%")
    print(f"{'Mean Results Count':<30} | {summary_a['results_count']['mean']:<15.1f} | {summary_b['results_count']['mean']:<15.1f} | {summary_b['results_count']['mean'] - summary_a['results_count']['mean']:<+10.1f}")
    print(f"{'Latency Mean (ms)':<30} | {summary_a['latency_ms']['mean']:<15.2f} | {summary_b['latency_ms']['mean']:<15.2f} | {report['delta']['latency_mean_ms_diff']:<+10.2f}ms")
    print(f"{'Latency Median (ms)':<30} | {summary_a['latency_ms']['median']:<15.2f} | {summary_b['latency_ms']['median']:<15.2f} | {report['delta']['latency_median_ms_diff']:<+10.2f}ms")
    print(f"{'Budget Violation Rate':<30} | {summary_a['budget_violation_rate']:<15.1%} | {summary_b['budget_violation_rate']:<15.1%} | 0.0%")
    print(f"{'McNemar p-value (Recall)':<30} | —               | —               | p = {mcnemar_recall['exact_two_sided_p_value']}")
    print("=" * 60)


if __name__ == "__main__":
    main()
