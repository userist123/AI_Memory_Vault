#!/usr/bin/env python3
"""
run_agent_corpus.py — drive the whole 20-book corpus through the agent path.

This is the counterpart to the local-provider orchestrator: same corpus, same
gates, same report shape, but the reading is done by an agent (Antigravity,
Codex, ChatGPT) instead of a model provider. Nothing here calls a model. It
prepares work and it scores work.

    prepare   writes one <name>_chunks.json per book for the agent to read,
              plus a manifest saying what order to do them in
    collect   gates every <name>_candidates.json the agent wrote back and
              produces staging/corpus_run_report.json

## Order matters, and it is not the order the local run used

`prepare` sorts the books by chunk count, largest first, and that is the
single decision in this file worth arguing about.

`occurrences` — how many distinct sections of a book define the same term — is
the only ranking signal in this pipeline that carries information. Confidence
is 1.00 on nearly everything, including candidates whose evidence turned out to
be fabricated. Review happens in descending `occurrences`, with a floor of 3.

A concept cannot occur in three distinct sections of a book that has fewer than
three sections. The local run went smallest-first and its first five books had
1, 1, 4, 4 and 7 chunks; every one of them reported `candidates_at_ge_3: 0`,
which says nothing about the extractor and everything about the arithmetic.
The books where recurrence can be measured at all — Newell, Schacter, Minsky,
Soar — are the large ones, and they come first here.

So the early numbers from this run are comparable to nothing in the local run,
and that is deliberate: they are the numbers that can actually be read.

## Use

    python 30_SCRIPTS/ingestion/run_agent_corpus.py prepare \\
        --work-dir scratch/agent_corpus

    # the agent reads scratch/agent_corpus/<name>_chunks.json and writes
    # scratch/agent_corpus/<name>_candidates.json — see
    # 00_GOVERNANCE/coordination/BOOK_INGESTION_HANDOFF.md for the format

    python 30_SCRIPTS/ingestion/run_agent_corpus.py collect \\
        --work-dir scratch/agent_corpus --agent-label antigravity

`collect` is resumable and is meant to be run repeatedly: a book with no
candidates file yet is reported as pending, not as a book that yielded nothing.
Those two are very different and the report keeps them apart.
"""

from __future__ import annotations

import argparse
import json
import pathlib
import sys
from collections import Counter
from typing import Any

_HERE = pathlib.Path(__file__).resolve().parent
_REPO = _HERE.parents[1]
sys.path.insert(0, str(_HERE))

from gate_agent_candidates import gate  # noqa: E402
from model_extract_concepts import (  # noqa: E402
    load_slot_questions,
    split_into_structural_chunks,
)

MANIFEST = _HERE / "corpus_manifest.json"

#: A book with fewer sections than this cannot produce a candidate at the
#: review floor of occurrences>=3, no matter how well it is read. It is still
#: worth extracting — it just must not be read as evidence about yield.
MIN_CHUNKS_FOR_RECURRENCE = 3


def load_books() -> list[dict[str, str]]:
    return json.loads(MANIFEST.read_text(encoding="utf-8"))["books"]


def _chunks_for(
    book: dict[str, str], corpus_root: pathlib.Path
) -> tuple[pathlib.Path, list[dict[str, Any]] | None]:
    path = corpus_root / book["rel_path"]
    if not path.exists():
        return path, None
    return path, split_into_structural_chunks(
        path.read_text(encoding="utf-8", errors="replace")
    )


def prepare(args: argparse.Namespace) -> int:
    """Write the chunks for every book, largest first."""
    slots = load_slot_questions()
    args.work_dir.mkdir(parents=True, exist_ok=True)

    prepared: list[dict[str, Any]] = []
    missing: list[str] = []

    for book in load_books():
        path, chunks = _chunks_for(book, args.corpus_root)
        if chunks is None:
            missing.append(book["short_name"])
            continue
        if not chunks:
            #: Converted to a file that chunks to nothing. Loud, not skipped:
            #: this exact shape once let eight books report success while
            #: producing zero extractable text.
            missing.append(f'{book["short_name"]} (0 chunks)')
            continue

        out = args.work_dir / f'{book["short_name"]}_chunks.json'
        out.write_text(json.dumps({
            "source_file": book["rel_path"],
            "chunk_count": len(chunks),
            "slots": slots,
            "chunks": [
                {"chunk_index": i, "heading": c["heading"], "content": c["content"]}
                for i, c in enumerate(chunks)
            ],
        }, indent=2, ensure_ascii=False), encoding="utf-8")

        prepared.append({
            "short_name": book["short_name"],
            "rel_path": book["rel_path"],
            "chunks_file": str(out.relative_to(_REPO)) if out.is_relative_to(_REPO) else str(out),
            "total_chunks": len(chunks),
            "recurrence_measurable": len(chunks) >= MIN_CHUNKS_FOR_RECURRENCE,
        })

    prepared.sort(key=lambda b: -b["total_chunks"])
    for rank, book in enumerate(prepared, start=1):
        book["order"] = rank

    (args.work_dir / "manifest.json").write_text(
        json.dumps({"books": prepared}, indent=2, ensure_ascii=False), encoding="utf-8"
    )

    total = sum(b["total_chunks"] for b in prepared)
    flat = [b for b in prepared if not b["recurrence_measurable"]]
    print(f"prepared   {len(prepared)} books, {total:,} chunks -> {args.work_dir}")
    print(f"{'order':>5}  {'chunks':>6}  book")
    for b in prepared:
        mark = "" if b["recurrence_measurable"] else "   <- too few sections for occurrences>=3"
        print(f'{b["order"]:>5}  {b["total_chunks"]:>6}  {b["short_name"]}{mark}')
    if flat:
        print(f"\n{len(flat)} book(s) cannot reach the review floor on their own; "
              "extract them, but do not read their yield as a result.")
    if missing:
        print(f"\nnot prepared ({len(missing)}): {', '.join(missing)}", file=sys.stderr)
        return 1
    return 0


def collect(args: argparse.Namespace) -> int:
    """Gate whatever the agent has written back so far."""
    report: list[dict[str, Any]] = []
    pending: list[str] = []
    staging = _REPO / "staging"

    for book in load_books():
        name = book["short_name"]
        candidates = args.work_dir / f"{name}_candidates.json"
        if not candidates.exists():
            #: Not the same as a book that yielded nothing, and the report is
            #: not allowed to blur the two.
            pending.append(name)
            continue

        path, chunks = _chunks_for(book, args.corpus_root)
        if not chunks:
            print(f"{name}: source missing or unchunkable, skipped", file=sys.stderr)
            continue

        out = staging / f"{name}.json"
        rej = staging / f"{name}_rej.json"
        gate(argparse.Namespace(
            input_file=path,
            candidates=candidates,
            source_book=name,
            output_file=out,
            rejects_file=rej,
            agent_label=args.agent_label,
        ))

        rows = json.loads(out.read_text(encoding="utf-8"))
        rejected = json.loads(rej.read_text(encoding="utf-8"))
        histogram = Counter(r.get("occurrences", 1) for r in rows)
        report.append({
            "short_name": name,
            "rel_path": book["rel_path"],
            "total_chunks": len(chunks),
            "recurrence_measurable": len(chunks) >= MIN_CHUNKS_FOR_RECURRENCE,
            "candidates_submitted": len(json.loads(candidates.read_text(encoding="utf-8"))),
            "candidates_kept": len(rows),
            "candidates_at_ge_2": sum(1 for r in rows if r.get("occurrences", 1) >= 2),
            "candidates_at_ge_3": sum(1 for r in rows if r.get("occurrences", 1) >= 3),
            "occurrence_histogram": {str(k): v for k, v in sorted(histogram.items())},
            "total_rejections": len(rejected),
            "rejections_by_reason": dict(Counter(r["reason"] for r in rejected).most_common()),
            "agent_label": args.agent_label,
        })

    staging.mkdir(parents=True, exist_ok=True)
    out = staging / args.report_name
    out.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")

    kept = sum(b["candidates_kept"] for b in report)
    at3 = sum(b["candidates_at_ge_3"] for b in report)
    reasons: Counter[str] = Counter()
    for b in report:
        reasons.update(b["rejections_by_reason"])

    print(f"\ncollected  {len(report)} of {len(load_books())} books -> {out}")
    print(f"  kept                    {kept}")
    print(f"  at occurrences>=3       {at3}")
    print(f"  rejected                {sum(reasons.values())}  {dict(reasons.most_common())}")
    if pending:
        print(f"  pending ({len(pending)})         {', '.join(pending)}")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="command", required=True)

    p = sub.add_parser("prepare", help="write chunks for every book, largest first")
    p.add_argument("--work-dir", required=True, type=pathlib.Path)
    p.add_argument("--corpus-root", type=pathlib.Path, default=_REPO,
                   help="where 06_INBOX/Carti lives. The converted .txt files "
                        "are untracked, so a git worktree does not have them; "
                        "point this at the main checkout when running from one.")
    p.set_defaults(func=prepare)

    c = sub.add_parser("collect", help="gate the candidates written back so far")
    c.add_argument("--work-dir", required=True, type=pathlib.Path)
    c.add_argument("--corpus-root", type=pathlib.Path, default=_REPO,
                   help="where 06_INBOX/Carti lives; see prepare --corpus-root")
    c.add_argument("--agent-label", required=True,
                   help="what produced these candidates, recorded in every row")
    c.add_argument("--report-name", default="agent_corpus_run_report.json",
                   help="written under staging/; kept distinct from the "
                        "local run's report so the two can be compared")
    c.set_defaults(func=collect)

    args = ap.parse_args()
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
