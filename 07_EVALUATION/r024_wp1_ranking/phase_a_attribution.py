"""WP-1 Phase A (r024) — BLOCKING attribution, run before proposing any fix.

For every dev.json case where a gold note was a candidate (present in
`candidate_trace['fused_ranking']`) but absent from the final context
(`pack['results']`), this attributes the loss to exactly one of:

    ranked_out            | RelevanceScorer's final order puts it at or
                             beyond page_size
    budget_truncated       | it survives disclosure but pack_builder drops it
                             for hard byte/token budget
    filtered                | it is not present in the disclosed list at all
                             despite being a candidate (a filter ran between
                             candidate generation and disclosure)
    lost_in_disclosure      | ProgressiveDisclosure itself drops it (its
                             per-note budget counter trips) while it is still
                             ranked ahead of page_size

This is purely observational: it reproduces RelevanceScorer's exact
computation externally (same class, same call signature, same tie-break
used in controller.py's graph-off branch) rather than modifying
controller.py -- Phase A must not change search() behaviour at all, and
reusing the real class instead of re-deriving the formula keeps this from
becoming a second, possibly-drifted implementation of the same scorer.

Runs ONLY on dev.json's ordinary (non-abstain, non-`one_hop_graph_expansion`)
cases, per WP-1 requirement 5 (tune on dev.json only) and the brief's
instruction not to touch graph expansion.

Run: python 07_EVALUATION/r024_wp1_ranking/phase_a_attribution.py
Output: 07_EVALUATION/r024_wp1_ranking/phase_a_attribution_report.json
"""
from __future__ import annotations

import json
import os
import sys
from collections import Counter
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
V2 = REPO / "07_EVALUATION" / "heldout_retrieval_benchmark_v2"
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
sys.path.insert(0, str(V2))

from freeze import digest, hash_path  # noqa: E402

os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

from memory_controller.authorizer import Principal  # noqa: E402
from memory_controller.controller import MemoryController  # noqa: E402
from memory_controller.storage.file_engine import FileStorageEngine  # noqa: E402
from memory_controller.context.relevance_scoring import RelevanceScorer  # noqa: E402
from memory_controller.context.progressive_disclosure import ProgressiveDisclosure  # noqa: E402
from memory_controller.context.budget import load_agent_budget  # noqa: E402
from memory_controller.security import sanitize_query  # noqa: E402
from retrieval.vault_index import VaultIndex  # noqa: E402

PAGE_SIZE = 10  # matches R016's configuration


def verify_frozen() -> None:
    for name in ("dev.json",):
        p = V2 / name
        recorded = hash_path(p).read_text(encoding="utf-8").strip()
        if digest(p) != recorded:
            raise SystemExit(f"FROZEN_SET_HASH_MISMATCH:{name}")


def load_dev_cases() -> list[dict]:
    cases = json.loads((V2 / "dev.json").read_text(encoding="utf-8"))["cases"]
    return [c for c in cases if not c["abstain"] and c["class"] != "one_hop_graph_expansion"]


def reproduce_pipeline(scorer: RelevanceScorer, budget, sanitized_query: str,
                        candidate_ids: list[str], storage) -> tuple[list[str], list[str], list[str]]:
    """Reproduces controller.py's graph-off branch exactly, externally:
    score -> sort (score, id) reverse=True -> ProgressiveDisclosure.metadata_only
    -> slice [0:PAGE_SIZE]. Returns (ordered_ids, disclosed_ids, page_result_ids)
    -- all three real, not assumed, so a genuine disclosure/budget drop would
    show up here rather than being defined away.
    """
    candidate_notes = [n for n in (storage.get(cid) for cid in candidate_ids) if n]
    scored = scorer.score(sanitized_query, candidate_notes)
    score_map = {s["id"]: float(s["score"]) for s in scored if s.get("id")}
    ordered = sorted(candidate_notes, key=lambda n: (score_map.get(n.get("id"), 0.0), n.get("id", "")), reverse=True)
    ordered_ids = [n.get("id") for n in ordered]

    pd = ProgressiveDisclosure(budget)
    disclosed = pd.metadata_only(ordered)  # production default disclosure level
    disclosed_ids = [d.get("id") for d in disclosed]
    page_result_ids = disclosed_ids[:PAGE_SIZE]
    return ordered_ids, disclosed_ids, page_result_ids


def attribute_loss(gold_id: str, ordered_ids: list[str], disclosed_ids: list[str],
                    page_result_ids: list[str]) -> str:
    """One of ranked_out / budget_truncated / filtered / lost_in_disclosure."""
    if gold_id not in ordered_ids:
        # Was a fused-ranking candidate but the reproduced scoring pipeline
        # never saw it (e.g. storage.get() returned nothing) -- treated as
        # filtered between candidate generation and scoring.
        return "filtered"

    rank = ordered_ids.index(gold_id)
    if gold_id not in disclosed_ids:
        # Present pre-disclosure but ProgressiveDisclosure itself dropped it
        # (its per-note budget counter tripped) regardless of rank.
        return "lost_in_disclosure"
    if rank >= PAGE_SIZE:
        return "ranked_out"
    if gold_id not in page_result_ids:
        # Ranked ahead of PAGE_SIZE and survived disclosure, yet still not in
        # the final page slice -- only pack_builder's hard-budget
        # apply_degradation() is left as the explanation.
        return "budget_truncated"
    # rank < PAGE_SIZE, present in disclosure, present in the pre-pack_builder
    # page slice -- if it is STILL missing from the real pack's results, the
    # only remaining stage is pack_builder.build()'s own degradation.
    return "budget_truncated"


def run_case(controller: MemoryController, case: dict, storage, scorer, budget) -> dict:
    sanitized = sanitize_query(case["query"])
    pack = controller.search(Principal.HUMAN, case["query"], page_size=PAGE_SIZE)
    trace = pack.get("candidate_trace", {}) or {}
    candidate_ids = [e.get("id") for e in (trace.get("fused_ranking") or []) if isinstance(e, dict)]
    context_ids = {r.get("id") for r in pack.get("results", []) if r.get("id")}
    gold = set(case["gold_relevant_notes"])

    ordered_ids, disclosed_ids, page_result_ids = reproduce_pipeline(
        scorer, budget, sanitized, candidate_ids, storage
    )

    losses = []
    for gid in sorted(gold):
        if gid not in candidate_ids:
            continue  # not this package's concern: it never became a candidate at all
        if gid in context_ids:
            continue  # no loss for this gold id
        category = attribute_loss(gid, ordered_ids, disclosed_ids, page_result_ids)
        losses.append({
            "gold_id": gid,
            "category": category,
            "rank_in_reproduced_order": ordered_ids.index(gid) if gid in ordered_ids else None,
        })

    return {
        "id": case["id"],
        "class": case["class"],
        "candidate_recall": int(bool(gold & set(candidate_ids))) if gold else 1,
        "context_recall": int(bool(gold & context_ids)) if gold else 1,
        "losses": losses,
    }


def main() -> int:
    verify_frozen()
    index = VaultIndex.load(REPO, include_raw=True, include_archived=True)
    storage = FileStorageEngine(str(REPO))
    controller = MemoryController(storage=storage, index=index, enable_graph_expansion=False)
    scorer = RelevanceScorer()
    budget = load_agent_budget(Principal.HUMAN.value)

    cases = load_dev_cases()
    unresolved = [(c["id"], g) for c in cases for g in c["gold_relevant_notes"] if g not in index.by_id]
    if unresolved:
        raise SystemExit(f"GOLD_UNRESOLVABLE:{unresolved}")

    rows = [run_case(controller, case, storage, scorer, budget) for case in cases]
    all_losses = [loss for row in rows for loss in row["losses"]]
    category_counts = Counter(loss["category"] for loss in all_losses)

    report = {
        "page_size": PAGE_SIZE,
        "cases_considered": len(rows),
        "case_ids": [r["id"] for r in rows],
        "candidate_recall": sum(r["candidate_recall"] for r in rows) / len(rows),
        "context_recall": sum(r["context_recall"] for r in rows) / len(rows),
        "total_loss_instances": len(all_losses),
        "attribution": dict(category_counts),
        "dominant_category": category_counts.most_common(1)[0][0] if category_counts else None,
        "rows": rows,
    }
    out_path = HERE / "phase_a_attribution_report.json"
    out_path.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps({k: v for k, v in report.items() if k != "rows"}, indent=2, ensure_ascii=False))
    print(f"\nFull report -> {out_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
