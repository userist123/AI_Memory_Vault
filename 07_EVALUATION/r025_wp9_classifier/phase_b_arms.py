"""WP-9 Phase B (r025) — C1/C2/C3 classifier-filter arms on heldout + dev.

Phase A (`phase_a_attribution.py`) proved the collapse is systematic, not
anecdotal: any query containing "verified" or "classified" as ordinary text
triggers a QueryClassifier lifecycle filter for a stage with zero notes in
the real corpus, guaranteeing pool collapse. This script measures whether
softening that INFERRED filter (never an explicit caller-supplied one, and
never RAW exclusion -- see CLASSIFIER_FILTER_ARM_* in
packages/retrieval/context/retrieval.py) actually recovers recall, using the
exact three arms implemented there:

  C1 hard        -- production default, unchanged (inferred filter excludes)
  C2 boost       -- inferred filter reorders candidates, never excludes
  C3 conditional -- inferred filter narrows only if the unfiltered pool
                    would otherwise exceed candidate_limit

ranking_arm is pinned explicitly to RANKING_ARM_FUSED_SCORE (the CURRENT
production default per r025 WP-8) so this measurement reflects what
production traffic actually experiences, and so it does not silently drift
if the ranking default changes again later -- the same reasoning R016 (run_
production_arms.py) applies by pinning to RANKING_ARM_BASELINE for its own,
deliberately ranking-invariant, comparison.

Run: python 07_EVALUATION/r025_wp9_classifier/phase_b_arms.py
Output: 07_EVALUATION/r025_wp9_classifier/phase_b_arms_report.json
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
from run_production_arms import run_case, summarise, mcnemar, UNMEASURABLE  # noqa: E402

os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

from memory_controller.authorizer import Principal  # noqa: E402
from memory_controller.controller import MemoryController, RANKING_ARM_FUSED_SCORE  # noqa: E402
from memory_controller.storage.file_engine import FileStorageEngine  # noqa: E402
from memory_controller.context.retrieval import (  # noqa: E402
    CLASSIFIER_FILTER_ARM_HARD,
    CLASSIFIER_FILTER_ARM_BOOST,
    CLASSIFIER_FILTER_ARM_CONDITIONAL,
)
from retrieval.vault_index import VaultIndex  # noqa: E402

ARMS = {
    "C1_hard": CLASSIFIER_FILTER_ARM_HARD,
    "C2_boost": CLASSIFIER_FILTER_ARM_BOOST,
    "C3_conditional": CLASSIFIER_FILTER_ARM_CONDITIONAL,
}


def verify_frozen() -> None:
    for name in ("heldout.json", "dev.json"):
        p = V2 / name
        recorded = hash_path(p).read_text(encoding="utf-8").strip()
        if digest(p) != recorded:
            raise SystemExit(f"FROZEN_SET_HASH_MISMATCH:{name}")


def build(index, storage, classifier_filter_arm: str) -> MemoryController:
    return MemoryController(
        storage=storage,
        index=index,
        enable_graph_expansion=False,
        strict_graph_expansion=False,
        graph_expansion_budget=None,
        ranking_arm=RANKING_ARM_FUSED_SCORE,
        classifier_filter_arm=classifier_filter_arm,
    )


def load_cases() -> list[dict]:
    heldout = json.loads((V2 / "heldout.json").read_text(encoding="utf-8"))["cases"]
    dev = json.loads((V2 / "dev.json").read_text(encoding="utf-8"))["cases"]
    for c in heldout:
        c["_set"] = "heldout"
    for c in dev:
        c["_set"] = "dev"
    return heldout + dev


def main() -> int:
    verify_frozen()
    index = VaultIndex.load(REPO, include_raw=True, include_archived=True)
    storage = FileStorageEngine(str(REPO))
    cases = load_cases()

    report = {"arms": {}}
    arm_rows = {}
    for label, arm_value in ARMS.items():
        controller = build(index, storage, arm_value)
        rows, errors = [], []
        for case in cases:
            try:
                rows.append(run_case(controller, case, index))
            except Exception as exc:
                errors.append({"id": case["id"], "error": f"{type(exc).__name__}: {exc}"})
        arm_rows[label] = rows
        report["arms"][label] = {"summary": summarise(rows) if rows else None, "errors": errors}

    pairs = [("C1_hard", "C2_boost"), ("C1_hard", "C3_conditional")]
    report["mcnemar"] = {
        f"{a}_vs_{b}": {
            f: mcnemar(arm_rows[a], arm_rows[b], f)
            for f in ("candidate_recall", "context_recall", "answer_correctness")
        }
        for a, b in pairs
    }

    # Per-class breakdown restricted to the 8 cases Phase A flagged as
    # collapsing under C1 -- the only cases any of these arms could possibly
    # change, since _apply_classifier_filter_arm() is a no-op whenever
    # neither inferred_lifecycle nor inferred_types is non-empty.
    phase_a = json.loads((HERE / "phase_a_attribution_report.json").read_text(encoding="utf-8"))
    collapsed_ids = {r["id"] for r in phase_a["collapsed_rows"]}
    report["collapsed_case_ids"] = sorted(collapsed_ids)
    report["collapsed_case_detail"] = {
        label: [r for r in rows if r["id"] in collapsed_ids]
        for label, rows in arm_rows.items()
    }

    out_path = HERE / "phase_b_arms_report.json"
    out_path.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps({k: v for k, v in report.items() if k not in ("collapsed_case_detail",)}, indent=2, ensure_ascii=False)[:4000])
    print(f"\nFull report -> {out_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
