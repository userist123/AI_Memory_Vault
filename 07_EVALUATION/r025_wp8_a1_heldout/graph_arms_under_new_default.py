"""WP-8 (r025) — does the graph NO-GO still hold under the NEW ranking default?

R016 (run_production_arms.py) is deliberately pinned to RANKING_ARM_BASELINE
so its historical comparison stays valid regardless of what the production
ranking default becomes (see the comment in build() there). This script is
the separate, explicit question that pin defers: with ranking_arm left
UNSPECIFIED (i.e. resolving to whatever MemoryController's actual current
default is -- RANKING_ARM_FUSED_SCORE as of r025 WP-8), does graph
expansion still fail to help?

Run: python 07_EVALUATION/r025_wp8_a1_heldout/graph_arms_under_new_default.py
Output: 07_EVALUATION/r025_wp8_a1_heldout/graph_arms_under_new_default_report.json
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
from run_production_arms import run_case, summarise, mcnemar  # noqa: E402

os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

from memory_controller.authorizer import Principal  # noqa: E402
from memory_controller.controller import MemoryController  # noqa: E402
from memory_controller.storage.file_engine import FileStorageEngine  # noqa: E402
from retrieval.vault_index import VaultIndex  # noqa: E402


def verify_frozen() -> None:
    p = V2 / "heldout.json"
    recorded = hash_path(p).read_text(encoding="utf-8").strip()
    if digest(p) != recorded:
        raise SystemExit("FROZEN_SET_HASH_MISMATCH:heldout.json")


def build(index, storage, graph_on: bool) -> MemoryController:
    # ranking_arm deliberately NOT passed -- resolves to whatever
    # MemoryController's actual current production default is.
    return MemoryController(
        storage=storage,
        index=index,
        enable_graph_expansion=graph_on,
        strict_graph_expansion=graph_on,
        graph_expansion_budget=10 if graph_on else None,
    )


def main() -> int:
    verify_frozen()
    index = VaultIndex.load(REPO, include_raw=True, include_archived=True)
    storage = FileStorageEngine(str(REPO))
    cases = json.loads((V2 / "heldout.json").read_text(encoding="utf-8"))["cases"]

    report = {"arms": {}}
    arms = {}
    for label, graph_on in (("graph_off", False), ("graph_on", True)):
        controller = build(index, storage, graph_on)
        rows, errors = [], []
        for case in cases:
            try:
                rows.append(run_case(controller, case, index))
            except Exception as exc:
                errors.append({"id": case["id"], "error": f"{type(exc).__name__}: {exc}"})
        arms[label] = rows
        report["arms"][label] = {"summary": summarise(rows) if rows else None, "errors": errors}

    if arms["graph_off"] and arms["graph_on"]:
        report["mcnemar"] = {
            f: mcnemar(arms["graph_off"], arms["graph_on"], f)
            for f in ("candidate_recall", "context_recall", "answer_correctness")
        }

    out_path = HERE / "graph_arms_under_new_default_report.json"
    out_path.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps(report, indent=2, ensure_ascii=False)[:3000])
    print(f"\nFull report -> {out_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
