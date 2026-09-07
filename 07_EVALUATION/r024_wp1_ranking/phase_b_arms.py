"""WP-1 Phase B (r024) — ranking arms, each measured independently.

Runs A1-A4 (RANKING_ARM_* in controller.py) plus the baseline, each as its
own arm through the real `MemoryController.search()` (graph expansion off,
untouched), on dev.json's 8 ordinary cases (non-abstain, excluding
`one_hop_graph_expansion`) -- per WP-1 requirement 5 (tune on dev.json only)
and requirement 1 (two arms identical in everything but the variable under
test: only `ranking_arm` differs between runs here).

Reports candidate recall and context recall per arm, per class and overall,
including arms that make things worse -- per the brief's explicit
instruction not to filter losing arms out of the report.

Run: python 07_EVALUATION/r024_wp1_ranking/phase_b_arms.py
Output: 07_EVALUATION/r024_wp1_ranking/phase_b_arms_report.json
"""
from __future__ import annotations

import json
import os
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
V2 = REPO / "07_EVALUATION" / "heldout_retrieval_benchmark_v2"
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
sys.path.insert(0, str(V2))

from freeze import digest, hash_path  # noqa: E402

os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

from memory_controller.authorizer import Principal  # noqa: E402
from memory_controller.controller import (  # noqa: E402
    MemoryController,
    RANKING_ARM_BASELINE,
    RANKING_ARM_FUSED_SCORE,
    RANKING_ARM_NO_CONFIDENCE,
    RANKING_ARM_CONFIDENCE_TIEBREAK,
    RANKING_ARM_FUSED_PLUS_TIEBREAK,
)
from memory_controller.storage.file_engine import FileStorageEngine  # noqa: E402
from retrieval.vault_index import VaultIndex  # noqa: E402

PAGE_SIZE = 10
ARMS = [
    ("baseline", RANKING_ARM_BASELINE),
    ("A1_fused_score", RANKING_ARM_FUSED_SCORE),
    ("A2_no_confidence", RANKING_ARM_NO_CONFIDENCE),
    ("A3_confidence_tiebreak", RANKING_ARM_CONFIDENCE_TIEBREAK),
]
# A4 is added after A2 vs A3 are compared -- see main().


def verify_frozen() -> None:
    p = V2 / "dev.json"
    recorded = hash_path(p).read_text(encoding="utf-8").strip()
    if digest(p) != recorded:
        raise SystemExit("FROZEN_SET_HASH_MISMATCH:dev.json")


def load_dev_cases() -> list[dict]:
    cases = json.loads((V2 / "dev.json").read_text(encoding="utf-8"))["cases"]
    return [c for c in cases if not c["abstain"] and c["class"] != "one_hop_graph_expansion"]


def run_arm(arm_value: str, cases: list[dict], index, storage) -> list[dict]:
    controller = MemoryController(storage=storage, index=index, enable_graph_expansion=False, ranking_arm=arm_value)
    rows = []
    for case in cases:
        pack = controller.search(Principal.HUMAN, case["query"], page_size=PAGE_SIZE)
        trace = pack.get("candidate_trace", {}) or {}
        candidates = {e.get("id") for e in (trace.get("fused_ranking") or []) if isinstance(e, dict)}
        context = {r.get("id") for r in pack.get("results", []) if r.get("id")}
        gold = set(case["gold_relevant_notes"])
        rows.append({
            "id": case["id"],
            "class": case["class"],
            "candidate_recall": int(bool(gold & candidates)) if gold else 1,
            "context_recall": int(bool(gold & context)) if gold else 1,
            "resolved_ranking_arm": trace.get("ranking_arm"),
        })
    return rows


def summarise(rows: list[dict]) -> dict:
    n = len(rows)
    return {
        "n": n,
        "candidate_recall": sum(r["candidate_recall"] for r in rows) / n,
        "context_recall": sum(r["context_recall"] for r in rows) / n,
        "context_recall_hits": [r["id"] for r in rows if r["context_recall"]],
    }


def main() -> int:
    verify_frozen()
    index = VaultIndex.load(REPO, include_raw=True, include_archived=True)
    storage = FileStorageEngine(str(REPO))
    cases = load_dev_cases()
    unresolved = [(c["id"], g) for c in cases for g in c["gold_relevant_notes"] if g not in index.by_id]
    if unresolved:
        raise SystemExit(f"GOLD_UNRESOLVABLE:{unresolved}")

    report = {"n_cases": len(cases), "case_ids": [c["id"] for c in cases], "arms": {}}
    for label, arm_value in ARMS:
        rows = run_arm(arm_value, cases, index, storage)
        report["arms"][label] = {"rows": rows, "summary": summarise(rows)}
        print(f"{label:28s} candidate_recall={report['arms'][label]['summary']['candidate_recall']:.3f} "
              f"context_recall={report['arms'][label]['summary']['context_recall']:.3f}")

    # A4 = A1 (fused_score primary) + whichever of A2/A3's CONFIDENCE
    # STRATEGY the measurement favours on context_recall:
    #   A3 wins (confidence-as-tiebreak helps) -> fused_score + confidence
    #     tiebreak (RANKING_ARM_FUSED_PLUS_TIEBREAK).
    #   A2 wins or ties (confidence hurts even as a tiebreak) -> confidence
    #     contributes nothing, so A4 collapses to fused_score alone -- i.e.
    #     literally RANKING_ARM_FUSED_SCORE (A1) again. Reported as such
    #     rather than silently re-running A1 under the A4 label with no
    #     explanation, or forcing the tiebreak variant regardless of what
    #     A2 vs A3 actually showed.
    a2_recall = report["arms"]["A2_no_confidence"]["summary"]["context_recall"]
    a3_recall = report["arms"]["A3_confidence_tiebreak"]["summary"]["context_recall"]
    if a3_recall > a2_recall:
        a4_arm_value, a4_label, a4_reason = (
            RANKING_ARM_FUSED_PLUS_TIEBREAK, "fused_score + confidence tiebreak",
            f"A3 ({a3_recall:.3f}) beat A2 ({a2_recall:.3f}) on context_recall",
        )
    else:
        a4_arm_value, a4_label, a4_reason = (
            RANKING_ARM_FUSED_SCORE, "fused_score alone (== A1)",
            f"A2 ({a2_recall:.3f}) >= A3 ({a3_recall:.3f}) on context_recall: confidence adds nothing "
            "even as a tiebreak, so A1+A2/A3 collapses to A1 itself",
        )
    rows = run_arm(a4_arm_value, cases, index, storage)
    report["arms"]["A4_fused_plus_best_confidence_strategy"] = {
        "rows": rows,
        "summary": summarise(rows),
        "resolved_as": a4_label,
        "reason": a4_reason,
    }
    print(f"{'A4 (' + a4_label + ')':45s} candidate_recall={report['arms']['A4_fused_plus_best_confidence_strategy']['summary']['candidate_recall']:.3f} "
          f"context_recall={report['arms']['A4_fused_plus_best_confidence_strategy']['summary']['context_recall']:.3f}")

    out_path = HERE / "phase_b_arms_report.json"
    out_path.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    print(f"\nFull report -> {out_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
