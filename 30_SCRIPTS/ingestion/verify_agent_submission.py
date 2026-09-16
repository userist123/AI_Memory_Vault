#!/usr/bin/env python3
"""
verify_agent_submission.py — check a submission against the source text,
rather than against the report that came with it.

## Why

Every claim in this file is one that arrived as "verified" in an execution
summary and turned out to be false when the files were opened:

  3,342 rows reported as gated with zero rejections had been built from five
  sentence frames, and were merged into all sixteen ontology slot files before
  anyone looked

  a second submission reported the same numbers while the slot files were
  untouched — the merge had not run at all

  a book reported as fully processed contained none of its own subject

  occurrences was reported as 3 for a concept drawn from two sections, and as
  1 for a concept the book defines in 88

The reports were fluent and internally consistent. The only thing that
separated the real runs from the generated ones was grepping the evidence
quotes against the source. This does that, the same way every time, so the
check stops depending on who remembers to run it.

It verifies what can be verified mechanically. Whether a definition is *good*
is not in here, and no exit code should be read as saying it is.

## Use

    python 30_SCRIPTS/ingestion/verify_agent_submission.py --book <short_name>
    python 30_SCRIPTS/ingestion/verify_agent_submission.py --all

Exit code 0 when every check passes, 1 when any fails.
"""

from __future__ import annotations

import argparse
import json
import pathlib
import re
import sys
from collections import Counter, defaultdict
from typing import Any

_HERE = pathlib.Path(__file__).resolve().parent
_REPO = _HERE.parents[1]
sys.path.insert(0, str(_HERE))

MANIFEST = _HERE / "corpus_manifest.json"

#: Below this, a quote is not a quote. The gate itself accepts a 12-word
#: verbatim run covering 70%; this is the looser floor for the *report*, so a
#: near-miss is shown rather than silently passed.
MIN_VERBATIM_FRACTION = 0.70

#: Matches gate_agent_candidates. A batch of this many rows whose top five
#: openings cover more than MAX_OPENING_COVERAGE is machine-written.
OPENING_NGRAM = 4
MIN_ROWS_TO_JUDGE_OPENINGS = 10
MAX_OPENING_COVERAGE = 0.70


def _flat(text: str) -> str:
    return " ".join(str(text).split())


def _words(text: str) -> list[str]:
    return [w for w in re.sub(r"[^\w\s]", " ", text.lower()).split() if w]


def longest_run(quote: str, source: str) -> int:
    """Longest verbatim word run of the quote present in the source."""
    words = _flat(quote).split()
    best = 0
    for start in range(len(words)):
        for end in range(len(words), start + best, -1):
            if " ".join(words[start:end]) in source:
                best = max(best, end - start)
                break
    return best


def check_evidence(rows, source) -> dict[str, Any]:
    """The one check that has caught every fabrication so far."""
    exact, partial, failed = 0, [], []
    for row in rows:
        quote = _flat(row.get("evidence_quote", ""))
        if not quote:
            failed.append((row.get("concept"), 0, 0))
            continue
        if quote in source:
            exact += 1
            continue
        run = longest_run(quote, source)
        total = len(quote.split())
        entry = (row.get("concept"), run, total)
        (partial if total and run / total >= MIN_VERBATIM_FRACTION else failed).append(entry)
    return {"exact": exact, "partial": partial, "failed": failed,
            "ok": not failed, "total": len(rows)}


def check_definitions_written(rows, source) -> dict[str, Any]:
    """A definition present verbatim in the book was copied, not written."""
    copied = [r.get("concept") for r in rows if _flat(r.get("definition", "")) in source]
    return {"copied": copied, "ok": not copied}


def check_openings(rows) -> dict[str, Any]:
    """Real writing opens almost every definition differently, because the
    opening follows the concept. A generator picks from a short menu: two
    submissions arrived at exactly five distinct openings across 762 rows."""
    counts: Counter[str] = Counter()
    for row in rows:
        words = _words(row.get("definition", ""))
        if len(words) >= OPENING_NGRAM:
            counts[" ".join(words[:OPENING_NGRAM])] += 1
    total = sum(counts.values())
    if not total:
        return {"ok": True, "distinct": 0, "coverage": 0.0, "judged": False}
    coverage = sum(n for _, n in counts.most_common(5)) / total
    judged = total >= MIN_ROWS_TO_JUDGE_OPENINGS
    return {"ok": not judged or coverage <= MAX_OPENING_COVERAGE,
            "distinct": len(counts), "coverage": coverage,
            "judged": judged, "total": total}


def check_low_prose(candidates, chunks) -> dict[str, Any]:
    """Index pages and OCR damage. Reading Newell's name index is how
    "Parr of mr" and "My basic cdsirafioo" became candidates."""
    low = {c["chunk_index"] for c in chunks if c.get("low_prose")}
    used = {c.get("chunk_index") for c in candidates}
    hit = sorted(x for x in used & low if x is not None)
    return {"read": hit, "ok": not hit, "flagged_in_book": len(low)}


#: Below this many prose sections a book cannot produce a spread, so a flat
#: occurrences column there is arithmetic rather than a finding. Kandel 2001 has
#: four sections and every concept correctly came back at 1.
MIN_PROSE_CHUNKS_TO_JUDGE_SPREAD = 12


def check_occurrences(rows, candidates, prose_chunks: int = 999) -> dict[str, Any]:
    """occurrences must equal the distinct sections the concept was drawn from.

    It has been wrong in both directions: inflated by the same concept
    submitted twice out of one chunk, and flattened to exactly 3 across nine
    concepts while one of them appears in 78 sections.
    """
    drawn: dict[str, set] = defaultdict(set)
    for c in candidates:
        drawn[str(c.get("concept", "")).strip().lower()].add(c.get("chunk_index"))
    wrong = []
    for row in rows:
        key = str(row.get("concept", "")).strip().lower()
        reported = row.get("occurrences")
        actual = len(drawn.get(key, set()))
        if actual and reported != actual:
            wrong.append((row.get("concept"), reported, actual))
    values = sorted(r.get("occurrences", 0) for r in rows)
    flat = (len(set(values)) == 1 and len(values) >= 5
            and prose_chunks >= MIN_PROSE_CHUNKS_TO_JUDGE_SPREAD)
    return {"mismatched": wrong, "ok": not wrong and not flat,
            "flat_at": values[0] if flat else None,
            "spread": (values[0], values[-1]) if values else (0, 0)}


def verify(book: dict[str, str], corpus_root: pathlib.Path,
           work_dir: pathlib.Path, staging: pathlib.Path) -> tuple[bool, list[str]]:
    name = book["short_name"]
    out: list[str] = []

    chunks_file = work_dir / f"{name}_chunks.json"
    cand_file = work_dir / f"{name}_candidates.json"
    rows_file = staging / f"{name}.json"
    if not cand_file.exists():
        return True, [f"{name}: not submitted yet"]
    if not rows_file.exists():
        return False, [f"{name}: candidates written but never gated"]

    source = _flat((corpus_root / book["rel_path"]).read_text(
        encoding="utf-8", errors="replace"))
    chunks = json.loads(chunks_file.read_text(encoding="utf-8"))["chunks"]
    candidates = json.loads(cand_file.read_text(encoding="utf-8"))
    rows = json.loads(rows_file.read_text(encoding="utf-8"))

    ev = check_evidence(rows, source)
    df = check_definitions_written(rows, source)
    op = check_openings(rows)
    lp = check_low_prose(candidates, chunks)
    used = {c.get("chunk_index") for c in candidates}
    prose = [c for c in chunks if not c.get("low_prose")]
    oc = check_occurrences(rows, candidates, len(prose))
    coverage = len(used) / max(len(prose), 1)

    ok = all(x["ok"] for x in (ev, df, op, lp, oc))
    out.append(f"{'PASS' if ok else 'FAIL'}  {name}")
    out.append(f"    evidence      {ev['exact']}/{ev['total']} exact"
               + (f", {len(ev['partial'])} partial" if ev["partial"] else "")
               + (f", {len(ev['failed'])} NOT IN SOURCE" if ev["failed"] else ""))
    for concept, run, total in ev["failed"]:
        out.append(f"        FABRICATED?  {concept}: longest verbatim run {run}/{total} words")
    if df["copied"]:
        out.append(f"    definitions   COPIED FROM SOURCE: {df['copied']}")
    else:
        out.append("    definitions   none copied from the source")
    out.append(f"    openings      {op['distinct']} distinct, top 5 cover "
               f"{op['coverage']:.0%}" + ("" if op["judged"] else "  (too few rows to judge)"))
    out.append(f"    low_prose     {len(lp['read'])} read of {lp['flagged_in_book']} flagged"
               + (f"  -> {lp['read']}" if lp["read"] else ""))
    if oc["mismatched"]:
        out.append("    occurrences   MISMATCH (reported vs distinct sections drawn from):")
        for concept, rep, act in oc["mismatched"][:6]:
            out.append(f"        {concept}: reported {rep}, drawn from {act}")
    elif oc["flat_at"] is not None:
        out.append(f"    occurrences   FLAT at {oc['flat_at']} across every row — "
                   "the floor is not a target")
    else:
        out.append(f"    occurrences   consistent, spread {oc['spread'][0]}-{oc['spread'][1]}")
    out.append(f"    coverage      {len(used)} of {len(prose)} prose chunks ({coverage:.0%})")
    if not book.get("on_subject", True):
        out.append("    WARNING       book is flagged OFF SUBJECT; nothing from it should be trusted")
    return ok, out


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--book")
    ap.add_argument("--all", action="store_true")
    ap.add_argument("--work-dir", type=pathlib.Path,
                    default=_REPO / "scratch" / "agent_corpus")
    ap.add_argument("--staging", type=pathlib.Path, default=_REPO / "staging")
    ap.add_argument("--corpus-root", type=pathlib.Path, default=_REPO)
    args = ap.parse_args(argv)

    books = json.loads(MANIFEST.read_text(encoding="utf-8"))["books"]
    manifest = json.loads((args.work_dir / "manifest.json").read_text(encoding="utf-8"))["books"]
    subject = {b["short_name"]: b.get("on_subject", True) for b in manifest}
    for b in books:
        b["on_subject"] = subject.get(b["short_name"], True)

    if args.book:
        books = [b for b in books if b["short_name"] == args.book]
        if not books:
            raise SystemExit(f"no book named {args.book}")
    elif not args.all:
        raise SystemExit("pass --book <short_name> or --all")

    failures = 0
    for book in books:
        ok, lines = verify(book, args.corpus_root, args.work_dir, args.staging)
        if len(lines) == 1 and "not submitted" in lines[0]:
            continue
        print("\n".join(lines))
        print()
        failures += not ok
    print(f"{failures} book(s) failed verification")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
