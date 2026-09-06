"""candidate_generation_measurement.py -- r004 candidate-recall measurement.

Measures CANDIDATE RECALL specifically -- i.e. "does the relevant note make
it into the ranked candidate set at all" -- separated from answer
correctness (which downstream scoring/disclosure/LLM synthesis affects and
which this task deliberately does not touch: RelevanceScorer, progressive
disclosure and the pagination/scoring pipeline are all unmodified).

Two conditions are compared on the exact same synthetic corpora:

  OLD  -- storage.query() result sliced to the first `candidate_limit`
          notes in insertion order (the pre-r004 production behaviour;
          see git history of retrieval.py before this change).
  NEW  -- generate_candidates() (BM25 + entity RRF fusion) over the same
          filtered notes (the r004 production behaviour).

Corpus sizes are chosen to bracket this vault's own observed scale: a
`grep -rl "^lifecycle:"` over this repository at the time of writing found
~831 notes total, ~85 tagged ACTIVE. 50/200/500/1000 brackets "small
project vault" through "well past this vault's current size", so the
result is not overfit to today's corpus.

This is a candidate-recall measurement, not a claim about answer quality on
any real query set -- the corpus is synthetic. Where the real vault content
could not be used (see USAGE NOTE below), that is stated plainly rather than
implied away.

USAGE NOTE on real vault content: FileStorageEngine's canonical_folders
(00_CORE, 01_KNOWLEDGE, 02_PROJECTS, 03_PROCEDURES, 04_MEMORY, 05_RESOURCES,
99_SYSTEM) do not match this repository's actual top-level layout
(00_GOVERNANCE, 01_ARCHITECTURE, 02_PRODUCT, ...) -- a pre-existing,
orthogonal structural mismatch (see
01_ARCHITECTURE/knowledge/Retrieval_Bottleneck_P0_Empirical_Findings.md,
which independently measured this same candidate-generation bottleneck via
evaluation/retrieval_diagnostic_runner.py, and
07_EVALUATION/retrieval_fusion_experiment_spec.md for the follow-up fusion
proposal this task implements a lexical+entity-only subset of), out of
scope for this task to fix. Real-vault-content measurement was therefore
not possible without also fixing that mismatch; this script uses a
synthetic corpus sized to the real vault's observed note count instead and
says so explicitly in its output.

Run: python 07_EVALUATION/candidate_generation_measurement.py
Output: 07_EVALUATION/candidate_generation_measurement_report.json
"""
from __future__ import annotations

import json
import os
import random
import sys
import time
from pathlib import Path
from typing import Any, Dict, List

ROOT = Path(__file__).resolve().parents[1]
PACKAGES = ROOT / "03_IMPLEMENTATION" / "packages"
for p in (str(ROOT), str(PACKAGES)):
    if p not in sys.path:
        sys.path.insert(0, p)

from retrieval.context.candidate_generation import generate_candidates, DEFAULT_CANDIDATE_LIMIT  # noqa: E402

QUERY = "MemoryController candidate generation fuses BM25 lexical scores with entity overlap"
CORPUS_SIZES = [50, 200, 500, 1000]
CANDIDATE_LIMITS = [20, 50, 100, 200, 500]  # 20 = old hardcoded default; 200 = new default
INSERTION_DEPTHS_FRACTIONS = [0.0, 0.1, 0.4, 0.8, 0.999]  # relative position of the relevant note
TRIALS_PER_CONFIG = 5  # different random filler content per trial, same relevant note


def _filler_note(i: int, rng: random.Random) -> Dict[str, Any]:
    topics = ["gardening", "cooking", "weather", "travel", "sports", "music", "history", "finance"]
    words = rng.sample(topics, k=3)
    return {"id": f"filler-{i:05d}", "content": f"unrelated filler document about {' '.join(words)} number {i}"}


def _relevant_note() -> Dict[str, Any]:
    return {"id": "TARGET", "content": QUERY, "tags": ["MemoryController", "candidate_generation"]}


def _old_head_n(notes: List[Dict[str, Any]], candidate_limit: int) -> List[Dict[str, Any]]:
    """Pre-r004 production behaviour: storage.query()'s result, sliced to
    candidate_limit in whatever order storage returned it (insertion order
    for the in-memory/file engines) -- the query text is never read."""
    return notes[:candidate_limit]


def measure() -> Dict[str, Any]:
    rng = random.Random(1234)
    results = []
    timings = {}

    for corpus_size in CORPUS_SIZES:
        for depth_frac in INSERTION_DEPTHS_FRACTIONS:
            depth = min(corpus_size - 1, int(corpus_size * depth_frac))
            old_hits = 0
            new_hits = 0
            new_gen_time_total = 0.0
            for _trial in range(TRIALS_PER_CONFIG):
                notes = [_filler_note(i, rng) for i in range(corpus_size - 1)]
                notes.insert(depth, _relevant_note())

                for candidate_limit in CANDIDATE_LIMITS:
                    old_result = _old_head_n(notes, candidate_limit)
                    old_hit = any(n["id"] == "TARGET" for n in old_result)

                    t0 = time.perf_counter()
                    new_result, _trace = generate_candidates(QUERY, notes, candidate_limit)
                    new_gen_time_total += time.perf_counter() - t0
                    new_hit = any(n["id"] == "TARGET" for n in new_result)

                    results.append({
                        "corpus_size": corpus_size,
                        "insertion_depth": depth,
                        "insertion_depth_fraction": depth_frac,
                        "candidate_limit": candidate_limit,
                        "trial": _trial,
                        "old_head_n_recall": int(old_hit),
                        "new_bm25_entity_recall": int(new_hit),
                    })
            timings.setdefault(corpus_size, []).append(new_gen_time_total / (TRIALS_PER_CONFIG * len(CANDIDATE_LIMITS)))

    # Aggregate recall by (corpus_size, candidate_limit) across all insertion
    # depths and trials -- this is the headline number: "if a relevant note
    # could be anywhere in the corpus, what fraction of the time is it
    # actually retrievable at this candidate_limit?"
    agg: Dict[str, Dict[str, Any]] = {}
    for r in results:
        key = f"corpus={r['corpus_size']}, candidate_limit={r['candidate_limit']}"
        bucket = agg.setdefault(key, {"old_hits": 0, "new_hits": 0, "n": 0})
        bucket["old_hits"] += r["old_head_n_recall"]
        bucket["new_hits"] += r["new_bm25_entity_recall"]
        bucket["n"] += 1

    summary = []
    for key, b in agg.items():
        summary.append({
            "config": key,
            "old_head_n_recall": round(b["old_hits"] / b["n"], 3),
            "new_bm25_entity_recall": round(b["new_hits"] / b["n"], 3),
            "n_samples": b["n"],
        })

    avg_latency_ms = {
        str(size): round(1000 * sum(vals) / len(vals), 4) for size, vals in timings.items()
    }

    report = {
        "note": (
            "Synthetic corpus, sized to this vault's observed scale "
            "(~831 notes total, ~85 ACTIVE per a `grep -rl \"^lifecycle:\"` scan "
            "at the time of writing). This measures CANDIDATE RECALL only -- "
            "whether the relevant note enters the ranked candidate set -- not "
            "answer correctness, which this task does not touch."
        ),
        "query": QUERY,
        "default_candidate_limit_chosen": DEFAULT_CANDIDATE_LIMIT,
        "corpus_sizes": CORPUS_SIZES,
        "candidate_limits_tested": CANDIDATE_LIMITS,
        "insertion_depth_fractions_tested": INSERTION_DEPTHS_FRACTIONS,
        "trials_per_config": TRIALS_PER_CONFIG,
        "summary_by_config": summary,
        "avg_generate_candidates_latency_ms_by_corpus_size": avg_latency_ms,
        "raw_samples": results,
    }
    return report


def main() -> int:
    report = measure()
    out_path = Path(__file__).resolve().parent / "candidate_generation_measurement_report.json"
    out_path.write_text(json.dumps(report, indent=2), encoding="utf-8")

    print(f"Query: {report['query']}")
    print(f"Default candidate_limit chosen: {report['default_candidate_limit_chosen']}")
    print()
    print(f"{'config':<40} {'old_recall':>11} {'new_recall':>11}")
    for row in report["summary_by_config"]:
        print(f"{row['config']:<40} {row['old_head_n_recall']:>11} {row['new_bm25_entity_recall']:>11}")
    print()
    print("avg generate_candidates() latency by corpus size (ms):")
    for size, ms in report["avg_generate_candidates_latency_ms_by_corpus_size"].items():
        print(f"  corpus={size}: {ms} ms")
    print()
    print(f"Full report written to {out_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
