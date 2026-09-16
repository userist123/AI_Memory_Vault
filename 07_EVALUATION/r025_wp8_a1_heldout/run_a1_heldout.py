"""WP-8 (r025) — validate A1 (fused_score ranking) on held-out, not dev.

Both arms through the real MemoryController.search(), same corpus, same
principal, same page_size, graph expansion off (out of scope, per r024
WP-1) -- the only variable is `ranking_arm`. Uses the WP-11-corrected
scoring (UNMEASURABLE for abstain cases, excluded from every mean), not
r024's tautology-inflated one.

Threshold was pre-registered in WP8_PREREGISTRATION.md before this ran.

Run: python 07_EVALUATION/r025_wp8_a1_heldout/run_a1_heldout.py
Output: 07_EVALUATION/r025_wp8_a1_heldout/wp8_heldout_report.json
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
# Reuse the WP-11-corrected scoring/summary logic directly -- no second
# implementation of run_case/summarise/mcnemar.
from run_production_arms import run_case, summarise, mcnemar, UNMEASURABLE  # noqa: E402

os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

from memory_controller.authorizer import Principal  # noqa: E402
from memory_controller.controller import MemoryController, RANKING_ARM_BASELINE, RANKING_ARM_FUSED_SCORE  # noqa: E402
from memory_controller.storage.file_engine import FileStorageEngine  # noqa: E402
from retrieval.vault_index import VaultIndex  # noqa: E402


def verify_frozen() -> None:
    p = V2 / "heldout.json"
    recorded = hash_path(p).read_text(encoding="utf-8").strip()
    if digest(p) != recorded:
        raise SystemExit("FROZEN_SET_HASH_MISMATCH:heldout.json")


def main() -> int:
    verify_frozen()
    index = VaultIndex.load(REPO, include_raw=True, include_archived=True)
    storage = FileStorageEngine(str(REPO))
    cases = json.loads((V2 / "heldout.json").read_text(encoding="utf-8"))["cases"]
    unresolved = [(c["id"], g) for c in cases for g in c["gold_relevant_notes"] if g not in index.by_id]
    if unresolved:
        raise SystemExit(f"GOLD_UNRESOLVABLE:{unresolved}")

    report = {"corpus_notes": len(index), "storage_notes": len(storage.id_to_path), "arms": {}}
    arms = {}
    for label, arm_value in (("baseline", RANKING_ARM_BASELINE), ("A1_fused_score", RANKING_ARM_FUSED_SCORE)):
        controller = MemoryController(storage=storage, index=index, enable_graph_expansion=False, ranking_arm=arm_value)
        rows = [run_case(controller, case, index) for case in cases]
        arms[label] = rows
        report["arms"][label] = {"summary": summarise(rows), "rows": rows}

    report["mcnemar"] = {
        f: mcnemar(arms["baseline"], arms["A1_fused_score"], f)
        for f in ("candidate_recall", "context_recall", "answer_correctness")
    }

    baseline_ctx = report["arms"]["baseline"]["summary"]["ALL"]["context_recall"]
    a1_ctx = report["arms"]["A1_fused_score"]["summary"]["ALL"]["context_recall"]
    n_measurable = report["arms"]["baseline"]["summary"]["ALL"]["n_measurable"]
    baseline_hits = round(baseline_ctx * n_measurable)
    a1_hits = round(a1_ctx * n_measurable)
    delta_cases = a1_hits - baseline_hits

    if delta_cases >= 2:
        decision = "CONFIRMED — flip default to fused_score"
    elif delta_cases <= 0:
        decision = "DISCONFIRMED — leave default at baseline, report dev/held-out gap"
    else:
        decision = "INCONCLUSIVE (+1 case, inside noise band) — leave default off pending more data"

    report["decision"] = {
        "baseline_context_recall_hits": baseline_hits,
        "a1_context_recall_hits": a1_hits,
        "n_measurable": n_measurable,
        "delta_cases": delta_cases,
        "verdict": decision,
    }

    out_path = HERE / "wp8_heldout_report.json"
    out_path.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps({
        "baseline_ALL": report["arms"]["baseline"]["summary"]["ALL"],
        "A1_ALL": report["arms"]["A1_fused_score"]["summary"]["ALL"],
        "mcnemar": report["mcnemar"],
        "decision": report["decision"],
    }, indent=2))
    print(f"\nFull report -> {out_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
