"""Measure the five ranking arms on benchmark v3, at both operating points.

Preregistered in 07_EVALUATION/ranking_formula/PREREGISTRATION.md, committed
before this script existed. Every number in the report is read from
results.json; none is written by hand.

The reason this run exists: every figure the project quotes was measured on
`RANKING_ARM_BASELINE`, while `search()` defaults to `RANKING_ARM_FUSED_SCORE`
(controller.py:942). The arm only takes effect on the graph-OFF branch, so
graph expansion is off here — which is also what the preregistration fixed.

Three controls run before any arm is compared, because three experiments in
this project have already reported null results from arms that could not move
the metric:

  sabotage      the arm's sort key is replaced by a constant; if the output
                does not change, that arm does not drive ranking
  distinctness  two arms with different keys must differ on at least one case,
                or they are reported as indistinguishable, never as equal
  determinism   three identical runs per arm

    python 30_SCRIPTS/evaluation/run_ranking_arm_experiment.py
"""
from __future__ import annotations

import hashlib
import json
import math
import os
import sys
from pathlib import Path
from typing import Any, Dict, List

REPO = Path(__file__).resolve().parents[2]
BENCH_JSON = REPO / "07_EVALUATION" / "retrieval_benchmark_v3" / "retrieval_benchmark_v3.json"
BENCH_SHA = REPO / "07_EVALUATION" / "retrieval_benchmark_v3" / "retrieval_benchmark_v3.json.sha256"
OUT_DIR = REPO / "07_EVALUATION" / "ranking_formula"
RESULTS_JSON = OUT_DIR / "results.json"

sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

from memory_controller.authorizer import Principal  # noqa: E402
from memory_controller import controller as controller_mod  # noqa: E402
from memory_controller.controller import (  # noqa: E402
    MemoryController,
    RANKING_ARM_BASELINE,
    RANKING_ARM_CONFIDENCE_TIEBREAK,
    RANKING_ARM_FUSED_PLUS_TIEBREAK,
    RANKING_ARM_FUSED_SCORE,
    RANKING_ARM_NO_CONFIDENCE,
)
from memory_controller.storage.file_engine import FileStorageEngine  # noqa: E402
from retrieval.vault_index import VaultIndex  # noqa: E402

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

UNMEASURABLE = "UNMEASURABLE"

ARMS = (
    RANKING_ARM_BASELINE,
    RANKING_ARM_FUSED_SCORE,
    RANKING_ARM_NO_CONFIDENCE,
    RANKING_ARM_CONFIDENCE_TIEBREAK,
    RANKING_ARM_FUSED_PLUS_TIEBREAK,
)

#: The two operating points the project has confused before. Named, so a
#: figure can never again be quoted without the point it was measured at.
OPERATING_POINTS = {
    "agent": {"principal": Principal.AI_AGENT, "page_size": 5},
    "human": {"principal": Principal.HUMAN, "page_size": 10},
}
PRODUCTION_POINT = "agent"
PRODUCTION_ARM = RANKING_ARM_FUSED_SCORE


# --- statistics ------------------------------------------------------------

def wilson(k: int, n: int) -> Dict[str, float]:
    if n == 0:
        return {"proportion": 0.0, "lower": 0.0, "upper": 0.0}
    p, z = k / n, 1.959963984540054
    d = 1.0 + z * z / n
    centre = (p + z * z / (2.0 * n)) / d
    margin = (z / d) * math.sqrt(p * (1.0 - p) / n + z * z / (4.0 * n * n))
    return {"proportion": round(p, 4), "lower": round(max(0.0, centre - margin), 4),
            "upper": round(min(1.0, centre + margin), 4)}


def exact_mcnemar_p(b: int, c: int) -> float:
    n = b + c
    if n == 0:
        return 1.0
    k = min(b, c)
    return min(1.0, 2.0 * sum(math.comb(n, i) for i in range(k + 1)) * 0.5 ** n)


def holm(pvalues: Dict[str, float]) -> Dict[str, Dict[str, Any]]:
    """Holm-Bonferroni over the arm comparisons, so four tests are not read as one."""
    ordered = sorted(pvalues.items(), key=lambda kv: kv[1])
    m = len(ordered)
    out, previous = {}, 0.0
    for i, (name, p) in enumerate(ordered):
        adjusted = max(previous, min(1.0, (m - i) * p))
        previous = adjusted
        out[name] = {"p_raw": round(p, 6), "p_holm": round(adjusted, 6),
                     "significant_at_0_05": adjusted < 0.05}
    return out


# --- running one case ------------------------------------------------------

def run_case(controller: MemoryController, case: dict, index: VaultIndex,
             principal: Principal, page_size: int, arm: str) -> dict:
    pack = controller.search(principal, case["query"], page_size=page_size,
                             ranking_arm=arm, enable_graph_expansion=False)
    trace = pack.get("candidate_trace", {}) or {}
    candidates = {e.get("id") for e in (trace.get("fused_ranking") or []) if isinstance(e, dict)}
    context = [r.get("id") for r in pack.get("results", []) if r.get("id")]
    gold = set(case.get("gold_relevant_notes") or [])

    row = {
        "id": case["id"],
        "class": case["class"],
        # The full ordered page, not a recall bit: the sabotage and distinctness
        # controls compare orders, and a bit cannot show a reordering.
        "order": context,
        "arm_in_trace": trace.get("ranking_arm"),
        "floor_applied": bool(trace.get("agent_lifecycle_floor_applied")),
    }
    if case.get("abstain"):
        row.update({"candidate_recall": UNMEASURABLE, "context_recall": UNMEASURABLE})
        return row
    row.update({
        "candidate_recall": int(bool(gold & candidates)) if gold else 1,
        "context_recall": int(bool(gold & set(context))) if gold else 1,
    })
    return row


def run_arm(storage, index, cases, point: str, arm: str) -> List[dict]:
    opts = OPERATING_POINTS[point]
    controller = MemoryController(storage=storage, index=index,
                                  enable_graph_expansion=False, ranking_arm=arm)
    return [run_case(controller, c, index, opts["principal"], opts["page_size"], arm)
            for c in cases]


# --- controls --------------------------------------------------------------

def run_arm_sabotaged(storage, index, cases, point: str, arm: str) -> List[dict]:
    """The same arm with its sort key flattened to a constant.

    `sorted` is stable, so a constant key leaves the pre-sort order untouched.
    If the sabotaged output equals the real one, the arm's key never decided
    anything and the arm's result is void.
    """
    original = controller_mod._ranking_key_fn
    controller_mod._ranking_key_fn = lambda *a, **k: (lambda n: 0)
    try:
        return run_arm(storage, index, cases, point, arm)
    finally:
        controller_mod._ranking_key_fn = original


def orders(rows: List[dict]) -> Dict[str, List[str]]:
    return {r["id"]: r["order"] for r in rows}


def cases_with_different_order(a: List[dict], b: List[dict]) -> List[str]:
    oa, ob = orders(a), orders(b)
    return sorted(cid for cid in oa.keys() & ob.keys() if oa[cid] != ob[cid])


# --- aggregation -----------------------------------------------------------

def summarise(rows: List[dict]) -> Dict[str, Any]:
    measurable = [r for r in rows if r["context_recall"] != UNMEASURABLE]
    ctx = sum(r["context_recall"] for r in measurable)
    cand = sum(r["candidate_recall"] for r in measurable)
    n = len(measurable)
    return {
        "n_queries": len(rows), "n_measurable": n, "n_unmeasurable": len(rows) - n,
        "context_recall": {"k": ctx, "n": n, "text": f"{ctx}/{n}"},
        "context_recall_ci": wilson(ctx, n),
        "candidate_recall": {"k": cand, "n": n, "text": f"{cand}/{n}"},
        "candidate_recall_ci": wilson(cand, n),
    }


def paired(reference: List[dict], arm: List[dict], field: str = "context_recall") -> Dict[str, Any]:
    by_id = {r["id"]: r for r in reference}
    won, lost, skipped = [], [], 0
    tied_1 = tied_0 = 0
    for r in arm:
        o = by_id.get(r["id"])
        if o is None or UNMEASURABLE in (r[field], o[field]):
            skipped += 1
            continue
        if o[field] == 0 and r[field] == 1:
            won.append(r["id"])
        elif o[field] == 1 and r[field] == 0:
            lost.append(r["id"])
        elif o[field] == 1:
            tied_1 += 1
        else:
            tied_0 += 1
    return {"gained": len(won), "lost": len(lost), "tied_both_1": tied_1,
            "tied_both_0": tied_0, "discordant": len(won) + len(lost),
            "skipped_unmeasurable": skipped,
            "exact_mcnemar_p": round(exact_mcnemar_p(len(lost), len(won)), 6),
            "winning_cases": won, "losing_cases": lost}


# --- decision rule, copied from the preregistration -----------------------

DECISION_RULE = (
    "Change the production default to arm X only if, against fused_score at the "
    "agent operating point, X gains >= 8 context_recall cases, loses <= 2, and "
    "exact two-sided McNemar gives p < 0.05. Otherwise keep the default."
)


def decide(comparisons: Dict[str, Any], holm_table: Dict[str, Any]) -> Dict[str, Any]:
    candidates = {}
    for arm, cmp in comparisons.items():
        passes = {
            "gains_ge_8": {"actual": cmp["gained"], "threshold": 8, "passed": cmp["gained"] >= 8},
            "losses_le_2": {"actual": cmp["lost"], "threshold": 2, "passed": cmp["lost"] <= 2},
            "mcnemar_p_lt_0_05": {"actual": cmp["exact_mcnemar_p"], "threshold": 0.05,
                                  "passed": cmp["exact_mcnemar_p"] < 0.05},
        }
        candidates[arm] = {"criteria": passes, "all_passed": all(c["passed"] for c in passes.values()),
                           "p_holm": holm_table.get(arm, {}).get("p_holm")}
    winners = [a for a, v in candidates.items() if v["all_passed"]]
    return {
        "rule_text": DECISION_RULE,
        "reference_arm": PRODUCTION_ARM,
        "operating_point": PRODUCTION_POINT,
        "per_arm": candidates,
        "arms_meeting_the_rule": winners,
        "verdict": (f"change default to {winners[0]}" if len(winners) == 1
                    else "keep the current default" if not winners
                    else f"several arms meet the rule: {winners}; owner decides"),
    }


# --- main ------------------------------------------------------------------

def main() -> int:
    expected = BENCH_SHA.read_text(encoding="utf-8").split()[0]
    actual = hashlib.sha256(BENCH_JSON.read_bytes()).hexdigest()
    if actual != expected:
        raise SystemExit(f"FROZEN_BENCHMARK_HASH_MISMATCH: {actual} != {expected}")

    cases = json.loads(BENCH_JSON.read_text(encoding="utf-8"))["cases"]

    cwd = os.getcwd()
    os.chdir(REPO)
    try:
        index = VaultIndex.load(Path("."), include_raw=True, include_archived=True)
        storage = FileStorageEngine(".")
    finally:
        os.chdir(cwd)

    print(f"BENCHMARK_SHA={actual}")
    print(f"CASES={len(cases)} NOTES_IN_INDEX={len(index.notes)}")

    results: Dict[str, Any] = {
        "schema": "ranking-arm-experiment.v1",
        "benchmark_sha256": actual,
        "n_cases": len(cases),
        "n_notes_in_index": len(index.notes),
        "preregistration": "07_EVALUATION/ranking_formula/PREREGISTRATION.md",
        "graph_expansion": False,
        "note": ("The ranking arm only affects the graph-OFF branch of search() "
                 "(controller.py:947), so graph expansion is off in every arm."),
        "points": {},
    }

    for point in OPERATING_POINTS:
        print(f"\n--- operating point: {point} ---")
        rows_by_arm, controls = {}, {}
        for arm in ARMS:
            runs = [run_arm(storage, index, cases, point, arm) for _ in range(3)]
            deterministic = orders(runs[0]) == orders(runs[1]) == orders(runs[2])
            rows = runs[0]
            sabotaged = run_arm_sabotaged(storage, index, cases, point, arm)
            changed = cases_with_different_order(rows, sabotaged)
            controls[arm] = {
                "deterministic_over_3_runs": deterministic,
                "sabotage_changed_cases": len(changed),
                "sabotage_proves_arm_is_live": len(changed) > 0,
                "arm_recorded_in_trace": sorted({r["arm_in_trace"] for r in rows}),
                "floor_applied_cases": sum(1 for r in rows if r["floor_applied"]),
            }
            rows_by_arm[arm] = rows
            s = summarise(rows)
            print(f"{arm:22} context_recall={s['context_recall']['text']:>8} "
                  f"deterministic={deterministic} sabotage_delta={len(changed)}")

        reference = rows_by_arm[PRODUCTION_ARM]
        comparisons = {a: paired(reference, rows_by_arm[a]) for a in ARMS if a != PRODUCTION_ARM}
        holm_table = holm({a: c["exact_mcnemar_p"] for a, c in comparisons.items()})

        distinctness = {}
        arm_list = list(ARMS)
        for i, a in enumerate(arm_list):
            for b in arm_list[i + 1:]:
                diff = cases_with_different_order(rows_by_arm[a], rows_by_arm[b])
                distinctness[f"{a}__vs__{b}"] = {
                    "cases_with_different_order": len(diff),
                    "status": "distinct" if diff else "indistinguishable_at_this_sample_size",
                }

        results["points"][point] = {
            "principal": OPERATING_POINTS[point]["principal"].name,
            "page_size": OPERATING_POINTS[point]["page_size"],
            "summaries": {a: summarise(rows_by_arm[a]) for a in ARMS},
            "controls": controls,
            "distinctness": distinctness,
            "paired_vs_production_arm": comparisons,
            "holm_bonferroni": holm_table,
            "rows": rows_by_arm,
        }

    results["decision"] = decide(
        results["points"][PRODUCTION_POINT]["paired_vs_production_arm"],
        results["points"][PRODUCTION_POINT]["holm_bonferroni"],
    )

    void = [f"{p}:{a}" for p, pv in results["points"].items()
            for a, c in pv["controls"].items() if not c["sabotage_proves_arm_is_live"]]
    results["void_arms"] = void

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    RESULTS_JSON.write_text(json.dumps(results, ensure_ascii=False, indent=2) + "\n",
                            encoding="utf-8", newline="\n")
    print(f"\nVERDICT={results['decision']['verdict']}")
    print(f"VOID_ARMS={void or 'none'}")
    print(f"written {RESULTS_JSON.relative_to(REPO)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
