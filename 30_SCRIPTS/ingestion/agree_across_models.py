#!/usr/bin/env python3
"""
agree_across_models.py — rank candidate concepts by how many independent
models found them.

## Why this exists

Every other ranking signal in this pipeline was measured and found empty:

  model confidence   1.00 on every candidate, including ones whose evidence
                     quote was not in the source at all
  claim_type         "definition" on every surviving candidate, in every run;
                     removed from the request entirely for that reason
  prompt exclusions  ignored by the model; the terms named as bad came back

Agreement is different in kind. It is **counted rather than claimed**, and no
single model can inflate it, because none of them knows what the others said.

## What it turned out NOT to be

An earlier version of this docstring listed `occurrences` among the empty
signals and presented agreement as the general answer to selectivity. Both
were measured further and both were wrong:

  occurrences   Not dead — measured at the wrong resolution. At 10 pages per
                chunk one chunk covers a chapter, so everything appears once.
                At 3 pages, a single instruct model's `occurrences >= 3` gives
                ~17 concepts per book at 70-92% precision against this tool's
                own 3-of-4 core, for a quarter of the compute. That is now the
                recommended path; see the ingestion procedure.

  agreement     Works on conference papers, where the 1-of-4 tail is
                experimental furniture and discarding it is pure gain. On
                monographs the same tail holds `long-term potentiation` and
                `memory consolidation`. The distribution barely moves between
                books — 4-of-4 at 6-9%, 1-of-4 at 66-70% — which makes it a
                property of the method rather than a measure of quality.

So this tool's job is narrower than it was written for: it produces the
reference set that a cheaper single-model rule is checked against, and it
annotates rather than filters. `--min-models` defaults to 1 for that reason.

## What it measures, on six chunks of one paper, four models

    found by 4 of 4   Reservoir sampling, Semantic memory, Synaptic
                      consolidation
    found by 3 of 4   Continual learning, Episodic memory, Fisher information
                      matrix, General-IL, stability-plasticity trade-off
    found by 1 of 4   Overall loss, Supervised loss, Soft-targets, reliability
                      plots, Representation space, Decision boundaries, ...

30 distinct concepts, 18 of them (60%) seen by exactly one model. That tail is
the experimental furniture no other mechanism could remove.

## What it costs, stated because it is not free

Two things, and neither should be discovered later:

1. **Compute scales with the number of models.** Four models over the 1,107
   chunk corpus is roughly 27 hours of local inference against about 7 for
   one.

2. **It loses true positives.** `Catastrophic forgetting` and `Experience
   replay` were each found by only one model in the measured run. Both are
   load-bearing. A threshold of 2 would discard them. Agreement measures how
   obvious a concept is to several readers, which is close to but not the
   same as how important it is.

So this is a ranking to review in order, not a gate to run unattended. The
default threshold is 1 — every concept is kept and merely annotated — because
a filter that silently drops real concepts should be something a person turns
on deliberately.
"""

from __future__ import annotations

import argparse
import json
import pathlib
import re
import sys
from collections import defaultdict
from typing import Any


def normalize_term(term: str) -> str:
    """Fold the differences that are formatting rather than meaning.

    Without this the count measures presentation: "Continual learning (CL)"
    and "continual learning" are the same concept and were emitted both ways
    in the same run, as were singular and plural forms.
    """
    text = re.sub(r"\([^)]*\)", " ", str(term).lower())
    text = re.sub(r"[^a-z0-9 ]", " ", text)
    return " ".join(_singular(w) for w in text.split())


def _singular(word: str) -> str:
    """Enough plural folding to match, not enough to merge distinct terms.

    A bare trailing-s strip is not enough and was wrong on the first real
    case it met: "approaches" became "approache" and failed to match
    "approach". Doing nothing is equally wrong — both spellings appeared in
    runs over the same corpus.
    """
    if len(word) <= 4:
        #: "loss", "bias", "mass" — stripping here does more harm than good.
        return word
    if word.endswith("ies"):
        return word[:-3] + "y"
    if word.endswith(("ches", "shes", "sses", "xes", "zes")):
        return word[:-2]
    if word.endswith("s") and not word.endswith("ss"):
        return word[:-1]
    return word


def load_runs(paths: list[pathlib.Path]) -> dict[str, dict[str, dict[str, Any]]]:
    """One mapping of normalized term -> row, per input file."""
    runs: dict[str, dict[str, dict[str, Any]]] = {}
    for path in paths:
        rows = json.loads(path.read_text(encoding="utf-8"))
        if not isinstance(rows, list):
            raise SystemExit(f"{path} is not a list of candidates")
        #: The model that produced the run, taken from the rows rather than
        #: the filename, so two runs of the same model cannot be counted as
        #: two independent votes.
        models = {str(r.get("model") or path.stem) for r in rows}
        name = sorted(models)[0] if len(models) == 1 else path.stem
        if name in runs:
            raise SystemExit(
                f"{name} appears twice; agreement between a model and itself "
                "is not evidence"
            )
        runs[name] = {normalize_term(r["concept"]): r for r in rows}
    return runs


def combine(runs: dict[str, dict[str, dict[str, Any]]]) -> list[dict[str, Any]]:
    """One row per distinct concept, annotated with who found it."""
    finders: dict[str, list[str]] = defaultdict(list)
    for model, rows in runs.items():
        for key in rows:
            finders[key].append(model)

    combined = []
    for key, models in finders.items():
        #: Keep the longest definition among the models that found it — in
        #: practice the one that actually explains the term rather than
        #: restating it.
        best = max(
            (runs[m][key] for m in models),
            key=lambda r: len(str(r.get("definition", ""))),
        )
        row = dict(best)
        row["models_agreeing"] = len(models)
        row["found_by"] = sorted(models)
        combined.append(row)

    combined.sort(
        key=lambda r: (-r["models_agreeing"], str(r["concept"]).lower())
    )
    return combined


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument(
        "runs", nargs="+", type=pathlib.Path,
        help="candidate JSON files, one per model, over the SAME chunks",
    )
    ap.add_argument("--output-file", required=True, type=pathlib.Path)
    ap.add_argument(
        "--min-models", type=int, default=1,
        help="keep only concepts found by at least this many models. Default "
             "1 keeps everything and only annotates: a threshold of 2 would "
             "have discarded 'catastrophic forgetting' and 'experience "
             "replay' in the measured run, so turning it on is a choice",
    )
    args = ap.parse_args()

    if len(args.runs) < 2:
        raise SystemExit("agreement needs at least two runs")

    runs = load_runs(args.runs)
    combined = combine(runs)

    kept = [r for r in combined if r["models_agreeing"] >= args.min_models]
    args.output_file.parent.mkdir(parents=True, exist_ok=True)
    args.output_file.write_text(json.dumps(kept, indent=2), encoding="utf-8")

    total = len(combined)
    print(f"models: {len(runs)}")
    for model, rows in sorted(runs.items()):
        print(f"  {model:26} {len(rows):>3} concepts")
    print(f"\ndistinct concepts: {total}")
    for n in range(len(runs), 0, -1):
        at_n = [r for r in combined if r["models_agreeing"] == n]
        if at_n:
            share = len(at_n) / total
            print(f"  found by {n}/{len(runs)}: {len(at_n):>3} ({share:.0%})")
    if args.min_models > 1:
        print(f"\nkept at min_models={args.min_models}: {len(kept)} of {total}")
        print(f"discarded: {total - len(kept)}")
    print(f"written: {args.output_file}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
