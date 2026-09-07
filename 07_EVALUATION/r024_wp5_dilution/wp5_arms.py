"""WP-5 (r024) — corpus dilution: do the 394 policy-lesson notes cost anything?

Measures candidate recall AND context recall (requirement 1) for three arms
on the same dev.json ordinary cases, WITHOUT any production code change --
WP-5's intent is `measure`, not `implement`. All three arms are simulated in
this script by calling `generate_candidates()` (the real, unmodified
candidate-generation function) directly over different note populations /
post-filters, then reproducing the same RelevanceScorer ranking + top-`page_size`
slice that Phase A (WP-1) established as an adequate proxy for the real
pipeline at this scale (zero budget/disclosure truncation observed there).

  B1  baseline    -- unmodified corpus, unmodified candidate generation.
  B2  excluded    -- policy-lesson notes (category == 'policy-lesson') never
                     enter the pool `generate_candidates()` ranks at all.
                     This is a retrieval-time exclusion for measurement only;
                     nothing is deleted from storage.
  B3  capped      -- policy-lesson notes retained in the corpus, but capped
                     at `POLICY_LESSON_CAP` (40, 20% of the 200-candidate
                     limit) within the final candidate set. Ranked normally
                     first; if more than the cap survive into the top
                     candidate_limit, the lowest-ranked excess is dropped and
                     backfilled with the next-best-ranked NON-policy-lesson
                     notes from the FULL ranked corpus (not just the
                     original top 200), so the cap changes composition, not
                     just count.

Run: python 07_EVALUATION/r024_wp5_dilution/wp5_arms.py
Output: 07_EVALUATION/r024_wp5_dilution/wp5_arms_report.json
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

from memory_controller.storage.file_engine import FileStorageEngine  # noqa: E402
from memory_controller.context.candidate_generation import generate_candidates, DEFAULT_CANDIDATE_LIMIT  # noqa: E402
from memory_controller.context.relevance_scoring import RelevanceScorer  # noqa: E402
from memory_controller.context.query_classifier import QueryClassifier  # noqa: E402
from memory_controller.security import sanitize_query  # noqa: E402
from retrieval.vault_index import VaultIndex  # noqa: E402

PAGE_SIZE = 10
POLICY_LESSON_CAP = 40  # 20% of DEFAULT_CANDIDATE_LIMIT (200)


def verify_frozen() -> None:
    p = V2 / "dev.json"
    recorded = hash_path(p).read_text(encoding="utf-8").strip()
    if digest(p) != recorded:
        raise SystemExit("FROZEN_SET_HASH_MISMATCH:dev.json")


def load_dev_cases() -> list[dict]:
    cases = json.loads((V2 / "dev.json").read_text(encoding="utf-8"))["cases"]
    return [c for c in cases if not c["abstain"] and c["class"] != "one_hop_graph_expansion"]


def is_policy_lesson(note: dict) -> bool:
    return note.get("category") == "policy-lesson"


def rank_and_slice(query: str, candidates: list[dict], scorer: RelevanceScorer) -> tuple[list[str], list[str]]:
    """Same reproduction Phase A used: RelevanceScorer -> sort(score, id) rev=True
    -> top page_size. Returns (candidate_ids, context_ids)."""
    sanitized = sanitize_query(query)
    scored = scorer.score(sanitized, candidates)
    score_map = {s["id"]: float(s["score"]) for s in scored if s.get("id")}
    ordered = sorted(candidates, key=lambda n: (score_map.get(n.get("id"), 0.0), n.get("id", "")), reverse=True)
    ordered_ids = [n.get("id") for n in ordered]
    return ordered_ids, ordered_ids[:PAGE_SIZE]


def arm_b1(query: str, all_notes: list[dict]) -> list[dict]:
    sanitized = sanitize_query(query)
    candidates, _ = generate_candidates(sanitized, all_notes, DEFAULT_CANDIDATE_LIMIT)
    return candidates


def arm_b2(query: str, all_notes: list[dict]) -> list[dict]:
    sanitized = sanitize_query(query)
    pool = [n for n in all_notes if not is_policy_lesson(n)]
    candidates, _ = generate_candidates(sanitized, pool, DEFAULT_CANDIDATE_LIMIT)
    return candidates


def arm_b3(query: str, all_notes: list[dict]) -> list[dict]:
    sanitized = sanitize_query(query)
    # Rank the FULL corpus (uncapped limit) so a backfill candidate beyond
    # the original top 200 is available if the cap displaces incumbents.
    full_ranked, _ = generate_candidates(sanitized, all_notes, len(all_notes))
    capped: list[dict] = []
    policy_count = 0
    overflow: list[dict] = []
    for note in full_ranked:
        if is_policy_lesson(note):
            if policy_count < POLICY_LESSON_CAP:
                capped.append(note)
                policy_count += 1
            # else: dropped, a non-policy backfill takes this rank instead
        else:
            capped.append(note)
        if len(capped) >= DEFAULT_CANDIDATE_LIMIT:
            break
    return capped[:DEFAULT_CANDIDATE_LIMIT]


ARMS = {"B1_baseline": arm_b1, "B2_excluded": arm_b2, "B3_capped": arm_b3}


def run_case(case: dict, storage, classifier: QueryClassifier, scorer: RelevanceScorer) -> dict:
    # Reproduce retrieve()'s actual hard gate per-case: the query classifier
    # can add lifecycle/type filters (4 of these 8 cases do -- D05 ACTIVE,
    # D06 target_type=experience, D07 VERIFIED, D08 CLASSIFIED+decision).
    # Applying one filter set to every case regardless of its own query was
    # checked and rejected before running: it would silently change which
    # notes each case's arms are even allowed to see.
    classified = classifier.classify(sanitize_query(case["query"]))
    all_notes = storage.query(
        intent=None,
        lifecycle=classified.get("lifecycle_filters") or None,
        types=classified.get("target_types") or None,
    )
    gold = set(case["gold_relevant_notes"])
    row = {"id": case["id"], "class": case["class"], "gated_pool_size": len(all_notes)}
    for arm_name, arm_fn in ARMS.items():
        candidates = arm_fn(case["query"], all_notes)
        candidate_ids = {n.get("id") for n in candidates}
        _, context_ids = rank_and_slice(case["query"], candidates, scorer)
        context_ids = set(context_ids)
        row[f"{arm_name}_candidate_recall"] = int(bool(gold & candidate_ids)) if gold else 1
        row[f"{arm_name}_context_recall"] = int(bool(gold & context_ids)) if gold else 1
        row[f"{arm_name}_policy_lesson_candidate_share"] = (
            round(sum(1 for n in candidates if is_policy_lesson(n)) / len(candidates), 3) if candidates else 0.0
        )
    return row


def summarise(rows: list[dict]) -> dict:
    n = len(rows)
    out = {}
    for arm_name in ARMS:
        out[arm_name] = {
            "candidate_recall": sum(r[f"{arm_name}_candidate_recall"] for r in rows) / n,
            "context_recall": sum(r[f"{arm_name}_context_recall"] for r in rows) / n,
            "mean_policy_lesson_candidate_share": sum(r[f"{arm_name}_policy_lesson_candidate_share"] for r in rows) / n,
        }
    return out


def main() -> int:
    verify_frozen()
    index = VaultIndex.load(REPO, include_raw=True, include_archived=True)
    storage = FileStorageEngine(str(REPO))
    scorer = RelevanceScorer()
    classifier = QueryClassifier()

    unfiltered_pool = storage.query(intent=None, lifecycle=None, types=None)
    policy_lesson_count = sum(1 for n in unfiltered_pool if is_policy_lesson(n))
    print(f"corpus (storage-visible, RAW excluded, no lifecycle/type filter): {len(unfiltered_pool)} notes, "
          f"{policy_lesson_count} policy-lesson ({policy_lesson_count/len(unfiltered_pool):.1%})")

    cases = load_dev_cases()
    unresolved = [(c["id"], g) for c in cases for g in c["gold_relevant_notes"] if g not in index.by_id]
    if unresolved:
        raise SystemExit(f"GOLD_UNRESOLVABLE:{unresolved}")

    rows = [run_case(case, storage, classifier, scorer) for case in cases]
    summary = summarise(rows)

    report = {
        "corpus_notes_storage_visible_unfiltered": len(unfiltered_pool),
        "policy_lesson_notes": policy_lesson_count,
        "policy_lesson_share_of_corpus": round(policy_lesson_count / len(unfiltered_pool), 3),
        "policy_lesson_cap": POLICY_LESSON_CAP,
        "page_size": PAGE_SIZE,
        "n_cases": len(cases),
        "case_ids": [c["id"] for c in cases],
        "summary": summary,
        "rows": rows,
    }
    out_path = HERE / "wp5_arms_report.json"
    out_path.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps(summary, indent=2))
    print(f"\nFull report -> {out_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
