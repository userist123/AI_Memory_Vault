"""Paired comparison of two AKE retrieval_quality results (baseline vs candidate).

Comparable only when both runs completed the stage on the same frozen benchmark digest, k and
principal. The test is McNemar's exact (binomial) test on the discordant cases; abstain or
otherwise unmeasurable cases (hit None) are skipped, never coerced to 0.

Verdicts: NOT_COMPARABLE, NO_CHANGE (no discordant case), IMPROVED / REGRESSED (two-sided exact
p < ALPHA), INCONCLUSIVE (discordant cases exist, p >= ALPHA). ALPHA is the conventional 0.05,
recorded in every result; it says whether a difference is distinguishable from chance on this
benchmark, not whether it is large enough to matter: that threshold is the owner's to set.
"""
from __future__ import annotations

from math import comb
from typing import Any, Dict, Optional

ALPHA = 0.05


def _stage(record: Dict[str, Any]) -> Optional[Dict[str, Any]]:
    if not isinstance(record, dict) or record.get("status") not in {"COMPLETED", "COMPLETED_WITH_SKIPS"}:
        return None
    for stage in record.get("stages") or []:
        if stage.get("name") == "retrieval_quality" and stage.get("status") == "COMPLETED":
            return stage.get("output")
    return None


def exact_mcnemar_p(b: int, c: int) -> float:
    n = b + c
    if n == 0:
        return 1.0
    tail = sum(comb(n, i) for i in range(min(b, c) + 1)) / 2 ** n
    return min(1.0, 2 * tail)


def compare_records(baseline: Dict[str, Any], candidate: Dict[str, Any]) -> Dict[str, Any]:
    base, cand = _stage(baseline), _stage(candidate)
    out: Dict[str, Any] = {"baseline_job": (baseline or {}).get("job_id"),
                           "candidate_job": (candidate or {}).get("job_id"), "alpha": ALPHA}
    if base is None or cand is None:
        return {**out, "verdict": "NOT_COMPARABLE", "reason": "a run has no completed retrieval_quality stage"}
    for key, a, b in (("benchmark digest", base["benchmark"]["digest"], cand["benchmark"]["digest"]),
                      ("k", base["summary"]["k"], cand["summary"]["k"]),
                      ("principal", base["summary"]["principal"], cand["summary"]["principal"])):
        if a != b:
            return {**out, "verdict": "NOT_COMPARABLE", "reason": f"{key} differs: {a} vs {b}"}
    by_id = {r["id"]: r["hit"] for r in base["cases"]}
    baseline_only = candidate_only = skipped = paired = 0
    for row in cand["cases"]:
        if row["id"] not in by_id:
            continue
        x, y = by_id[row["id"]], row["hit"]
        if x is None or y is None:
            skipped += 1
            continue
        paired += 1
        if x == 1 and y == 0:
            baseline_only += 1
        elif x == 0 and y == 1:
            candidate_only += 1
    discordant = baseline_only + candidate_only
    p = exact_mcnemar_p(baseline_only, candidate_only)
    if discordant == 0:
        verdict = "NO_CHANGE"
    elif p < ALPHA:
        verdict = "IMPROVED" if candidate_only > baseline_only else "REGRESSED"
    else:
        verdict = "INCONCLUSIVE"
    return {**out, "verdict": verdict, "n_paired": paired, "skipped_unmeasurable": skipped,
            "baseline_only": baseline_only, "candidate_only": candidate_only,
            "discordant": discordant, "p_value": p,
            "baseline_hits": sum(1 for r in base["cases"] if r["hit"] == 1),
            "candidate_hits": sum(1 for r in cand["cases"] if r["hit"] == 1)}
