#!/usr/bin/env python3
"""
mention_worksheet.py — find every section that mentions a submitted concept,
so the reader only has to judge which ones define it.

## Why

`occurrences` — how many distinct sections of a book define a term — is the
only ranking signal in this pipeline that carries information. Confidence is
1.00 on nearly everything, including candidates whose evidence was fabricated.

Three agent runs produced three useless versions of that number:

    Newell   the same concept submitted twice from one chunk, counted twice
    Soar     one concept per chunk, never repeated: every row occurrences=1
    Minsky   nine of ten concepts at exactly 3, the review floor, while
             "agent" appears in 84 of 107 sections and "cross-exclusion" in 5

Each was a reasonable response to the previous correction, over-applied. That
is the shape of an instruction problem, but it is not only an instruction
problem: tracking which of 107 sections mentioned a term, while reading them,
is a memory task. Asked to do it from memory an agent will approximate, and the
approximation collapses onto whatever number was last emphasised.

So this does the remembering. It takes the candidates already submitted, finds
every section of the book that mentions each concept, and writes a worksheet.
What is left is the judgement that actually needs a reader:

    **does this passage define the term, or merely mention it?**

A mention is not an occurrence. "Agents" appears on nearly every page of *The
Society of Mind* and is defined in a handful of them. The worksheet deliberately
does not guess: it lists, with enough surrounding text to decide, and every
entry starts unanswered.

## Use

    python 30_SCRIPTS/ingestion/mention_worksheet.py \\
        --chunks scratch/agent_corpus/<book>_chunks.json \\
        --candidates scratch/agent_corpus/<book>_candidates.json \\
        --output scratch/agent_corpus/<book>_worksheet.json

Then, for each section the worksheet lists that does define the term, submit a
candidate from it with its own evidence quote drawn from that section. Sections
where the term is only mentioned are left alone.
"""

from __future__ import annotations

import argparse
import json
import pathlib
import re
from typing import Any

#: Characters of surrounding text per mention. Enough to tell a definition from
#: a passing reference without turning the worksheet into a second copy of the
#: book: a definition almost always announces itself within a sentence or two.
CONTEXT_CHARS = 260

#: More than this in one section and the section is about the term, which is
#: itself worth knowing; the worksheet says so rather than printing all of them.
MAX_SNIPPETS_PER_CHUNK = 3


def variants(concept: str) -> list[str]:
    """The forms a term actually appears in.

    Written from the corpus rather than from grammar: the book writes
    "Papert's Principle" where the candidate says "papert principle", and
    "K-lines" where it says "k-line". Matching only the submitted string missed
    every one of those.
    """
    base = re.sub(r"\s+", " ", concept.strip().lower())
    forms = {base}
    if base.endswith("s"):
        forms.add(base[:-1])
    else:
        forms.add(base + "s")
    #: "papert principle" -> "papert's principle", and the reverse.
    words = base.split()
    if len(words) > 1:
        forms.add(" ".join([words[0] + "'s"] + words[1:]))
    forms.add(base.replace("'s ", " "))
    forms.add(base.replace("-", " "))
    forms.add(base.replace(" ", "-"))
    return sorted(forms)


def _pattern(concept: str) -> re.Pattern[str]:
    """Whole-word-ish match on any variant, apostrophes and hyphens allowed."""
    alts = sorted(variants(concept), key=len, reverse=True)
    body = "|".join(re.escape(a).replace(r"\ ", r"[\s\-]+") for a in alts)
    return re.compile(rf"(?<![\w-])({body})(?![\w-])", re.IGNORECASE)


def find_mentions(chunks: list[dict[str, Any]], concept: str) -> list[dict[str, Any]]:
    """Every non-low_prose section mentioning the term, with context."""
    pattern = _pattern(concept)
    found: list[dict[str, Any]] = []
    for chunk in chunks:
        if chunk.get("low_prose"):
            #: Indexes name every term in the book and define none of them.
            continue
        content = chunk["content"]
        hits = list(pattern.finditer(content))
        if not hits:
            continue
        snippets = []
        for hit in hits[:MAX_SNIPPETS_PER_CHUNK]:
            start = max(0, hit.start() - CONTEXT_CHARS // 2)
            end = min(len(content), hit.end() + CONTEXT_CHARS // 2)
            snippets.append("…" + " ".join(content[start:end].split()) + "…")
        found.append({
            "chunk_index": chunk["chunk_index"],
            "heading": chunk["heading"][:120],
            "mentions_in_chunk": len(hits),
            "snippets": snippets,
            #: Unanswered on purpose. A mention is not an occurrence, and
            #: nothing here can tell the difference.
            "defines_the_term": None,
        })
    return found


def build(args: argparse.Namespace) -> int:
    payload = json.loads(args.chunks.read_text(encoding="utf-8"))
    chunks = payload["chunks"]
    candidates = json.loads(args.candidates.read_text(encoding="utf-8"))
    if not isinstance(candidates, list):
        raise SystemExit("candidates file must be a JSON list")

    submitted: dict[str, set[int]] = {}
    for candidate in candidates:
        if not isinstance(candidate, dict):
            continue
        concept = str(candidate.get("concept", "")).strip()
        if not concept:
            continue
        submitted.setdefault(concept.lower(), set()).add(candidate.get("chunk_index"))

    rows: list[dict[str, Any]] = []
    for concept, done in sorted(submitted.items()):
        mentions = find_mentions(chunks, concept)
        unchecked = [m for m in mentions if m["chunk_index"] not in done]
        rows.append({
            "concept": concept,
            "already_submitted_from": sorted(x for x in done if isinstance(x, int)),
            "sections_mentioning": len(mentions),
            "sections_not_yet_looked_at": [m["chunk_index"] for m in unchecked],
            "mentions": unchecked,
        })

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps({
        "source_chunks": str(args.chunks),
        "question": ("For each section listed, does the passage DEFINE or "
                     "EXPLAIN the term, or does it merely mention it? Submit a "
                     "candidate only from the sections that define it, each "
                     "with its own evidence quote from that section."),
        "concepts": rows,
    }, indent=2, ensure_ascii=False), encoding="utf-8")

    print(f"worksheet written   {args.output}")
    print(f"{'concept':32} {'submitted':>10} {'mentioned in':>13} {'to look at':>11}")
    for row in rows:
        print(f'{row["concept"][:32]:32} {len(row["already_submitted_from"]):>10} '
              f'{row["sections_mentioning"]:>13} {len(row["sections_not_yet_looked_at"]):>11}')

    spread = sorted(r["sections_mentioning"] for r in rows)
    if spread:
        print(f"\nmentions per concept: min {spread[0]}, median "
              f"{spread[len(spread) // 2]}, max {spread[-1]}")
        print("A mention is not an occurrence. These are sections to look at, "
              "not a count to copy — the whole value of `occurrences` is that "
              "the number differs between a central concept and a local one.")
    return 0


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    ap.add_argument("--chunks", required=True, type=pathlib.Path)
    ap.add_argument("--candidates", required=True, type=pathlib.Path)
    ap.add_argument("--output", required=True, type=pathlib.Path)
    args = ap.parse_args(argv)
    return build(args)


if __name__ == "__main__":
    raise SystemExit(main())
