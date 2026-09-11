#!/usr/bin/env python3
"""
gate_agent_candidates.py — run the extraction gates over candidates an agent
produced directly, with no model provider in the loop.

## Why this exists

`model_extract_concepts.py` asks a local model for candidates and then
refuses most of them. On this hardware the models that fit are 7-8B, and the
refusals are the point: three fabricated evidence quotes in a single
six-chunk run, invented ontology slots, definitions that were the source
sentence with two words changed.

A capable agent reading the chunks itself should produce better candidates.
It should not produce *unchecked* ones. Every gate in this file exists
because something got past its absence:

  grounding   the evidence quote must appear in the chunk it claims to come
              from — a 12-word verbatim run covering 70% of the quote. An
              accurate citation that is absent from the ingested text is
              worse than no citation: it reads as a sound provenance chain
              and is not.

  paraphrase  no shared 8-gram with the evidence, and token overlap below
              0.60. Passing a verbatim guard is a floor, not a quality bar —
              r029 found promoted notes clearing a 15-word guard while still
              carrying 11-word verbatim runs.

  shape       malformed terms and truncated definitions are refused, never
              repaired. Repairing them is how the rule-based extractor
              produced 28% unusable rows.

  slot        one of the sixteen canonical slots, and nothing else.

Nothing here is model-specific, so the thresholds apply unchanged. They were
calibrated against measured populations rather than against a particular
model's weaknesses: apparent fabrications ran 3-6 verbatim words against
25-27 for real quotes, and a synonym-swap scored 0.68-0.72 token overlap.

**If a stronger model's rejection rate does not fall, that is information.**
It means the gates were not calibrated on small-model failure modes, and the
problem is somewhere else. The per-reason breakdown is printed for exactly
that comparison.

## How to use it

1. `chunks` — dump the book's chunks so the agent can read them and so the
   grounding check has the exact text that was read:

       python gate_agent_candidates.py chunks --input-file BOOK.txt \\
           --output-file chunks.json

2. The agent reads `chunks.json` and writes candidates as a JSON list. Each
   entry needs `chunk_index`, `concept`, `definition`, `evidence`, `slot`,
   `confidence`; `claim_type` is optional.

3. `gate` — run the checks and emit rows in the schema
   `merge_candidate_concepts.py` consumes:

       python gate_agent_candidates.py gate --input-file BOOK.txt \\
           --candidates agent.json --source-book NAME \\
           --output-file staging/NAME.json --rejects-file staging/NAME_rej.json

The grounding check reads the chunk by its index, so a candidate that names
the wrong chunk fails — which is the correct outcome, because its evidence
was then not verified against the passage it claims.
"""

from __future__ import annotations

import argparse
import json
import pathlib
import re
import sys
from collections import Counter
from typing import Any

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

from model_extract_concepts import (  # noqa: E402
    KNOWN_VERIFIED_MODULES,
    clean_term,
    deduplicate,
    format_slot_block,
    load_slot_questions,
    split_into_structural_chunks,
    validate,
)



FRAME_NGRAM = 8

#: Reported at 3 — mild phrase reuse is worth seeing, not worth refusing.
#: Measured on the real llama3.1:8b run, three or four definitions sharing an
#: 8-gram is a shared *domain* phrase, not a scaffold: "essential for the
#: stabilization of the spatial map" recurred across PKA, late LTP, protein
#: synthesis and gene expression, and each of those definitions still said
#: something specific about its own concept.
MAX_DEFINITION_FRAME_REUSE = 3

#: Refused at 10. The two populations separate cleanly here and nowhere else:
#: across 82 real candidates the most any 8-gram was reused was 4, while the
#: fabricated run reached 168, 109, 93, 89. Coverage was tried first and does
#: not work — the fabricated definitions sat at 0.47 of their tokens and the
#: real ones reached 0.83, so the distributions overlap and a coverage
#: threshold would convict the wrong rows.
FRAME_REUSE_ROW_LIMIT = 10

#: A whole submission is refused when this much of it carries reused phrasing.
#: Real books ran 0%, 0%, 0%, 0%, 0%, 33%, 50%; the fabricated run put thirteen
#: books at exactly 100%. The floor exists because the fraction means nothing
#: on a handful of rows — four candidates, three sharing a phrase, is 75% and
#: is not evidence of anything.
FRAME_REUSE_BATCH_FRACTION = 0.60
FRAME_REUSE_BATCH_FLOOR = 10


def _tokens(text: str) -> list[str]:
    return [w for w in re.sub(r"[^\w\s]", " ", text.lower()).split() if w]


def _frames_in(definition: str, n: int = FRAME_NGRAM) -> set[str]:
    """Every n-gram in one definition, deduplicated."""
    words = _tokens(definition)
    return {" ".join(words[i:i + n]) for i in range(len(words) - n + 1)}


def find_reused_frames(
    definitions: list[str],
    n: int = FRAME_NGRAM,
    limit: int = MAX_DEFINITION_FRAME_REUSE,
) -> dict[str, int]:
    """n-grams appearing in `limit` or more distinct definitions in one batch.

    The gates check each candidate against its evidence. Nothing checked the
    candidates against *each other*, and that is the hole 3,342 fabricated rows
    went through: five sentence frames, three words swapped into each, merged
    into the ontology with zero rejections.

        Characterizes an operational mechanism in which functional interactions
        govern X and Y and Z within the underlying system architecture.

    A frame like that shares no 8-gram with the evidence — it is not derived
    from the evidence at all — so the paraphrase gate passes it. The evidence is
    a verbatim copy, so grounding passes. The term is 1-4 words and the
    definition is over twelve ending in a period, so shape passes. Every gate
    was satisfied by construction rather than by the candidate saying anything.

    So this asks the reverse question: does this definition share its phrasing
    with its neighbours? Counting distinct definitions rather than occurrences
    keeps one repetitive definition from convicting itself.
    """
    from collections import defaultdict

    holders: dict[str, set[int]] = defaultdict(set)
    for index, definition in enumerate(definitions):
        words = _tokens(definition)
        #: A set per definition, so a phrase repeated inside one definition
        #: counts once.
        for start in range(len(words) - n + 1):
            holders[" ".join(words[start:start + n])].add(index)

    return {phrase: len(owners) for phrase, owners in holders.items()
            if len(owners) >= limit}


def dump_chunks(args: argparse.Namespace) -> int:
    """Write the chunks an agent should read, with the slot definitions."""
    text = args.input_file.read_text(encoding="utf-8", errors="replace")
    chunks = split_into_structural_chunks(text)
    if not chunks:
        print(f"no chunks in {args.input_file}", file=sys.stderr)
        return 1

    payload = {
        "source_file": str(args.input_file),
        "chunk_count": len(chunks),
        #: The slot questions, not just the names. Asked to place a concept
        #: against a bare list, a model picks by which name sounds closest;
        #: the questions are the actual selection criterion.
        "slots": load_slot_questions(),
        "chunks": [
            {"chunk_index": i, "heading": c["heading"], "content": c["content"]}
            for i, c in enumerate(chunks)
        ],
    }
    args.output_file.parent.mkdir(parents=True, exist_ok=True)
    args.output_file.write_text(json.dumps(payload, indent=2), encoding="utf-8")

    sizes = sorted(len(c["content"]) for c in chunks)
    print(f"chunks written   {len(chunks)}  -> {args.output_file}")
    print(f"  median {sizes[len(sizes) // 2]:,} chars, max {sizes[-1]:,}")
    print(format_slot_block(payload["slots"]))
    return 0


def gate(args: argparse.Namespace) -> int:
    """Run every candidate against the gates, against the chunk it names."""
    text = args.input_file.read_text(encoding="utf-8", errors="replace")
    chunks = split_into_structural_chunks(text)
    candidates = json.loads(args.candidates.read_text(encoding="utf-8"))
    if not isinstance(candidates, list):
        raise SystemExit("candidates file must be a JSON list")

    rows: list[dict[str, Any]] = []
    rejected: list[dict[str, Any]] = []
    rejects: Counter[str] = Counter()

    for candidate in candidates:
        if not isinstance(candidate, dict):
            rejects["not_an_object"] += 1
            continue

        index = candidate.get("chunk_index")
        if not isinstance(index, int) or not 0 <= index < len(chunks):
            #: Not a formality. Grounding is only meaningful against the text
            #: the candidate was actually drawn from; checking it against the
            #: wrong chunk would either pass by luck or fail for the wrong
            #: reason.
            rejects["chunk_index_invalid"] += 1
            rejected.append({"reason": "chunk_index_invalid", **_summarise(candidate)})
            continue

        chunk = chunks[index]
        ok, reason = validate(candidate, chunk["content"])
        if not ok:
            rejects[reason] += 1
            rejected.append({"reason": reason, **_summarise(candidate)})
            continue

        slot = str(candidate["slot"]).strip().lower()
        rows.append({
            "concept": clean_term(candidate["concept"]),
            "definition": str(candidate["definition"]).strip(),
            "claim_type": str(candidate.get("claim_type", "")).strip().lower() or None,
            "maps_to_slot": slot,
            "maps_to_module": (KNOWN_VERIFIED_MODULES.get(slot) or [None])[0],
            "confidence_in_literature": float(candidate["confidence"]),
            "source_book": args.source_book,
            "source_location": chunk["heading"][:120],
            "evidence_quote": str(candidate["evidence"]).strip(),
            "extraction_method": "agent_direct",
            "model": args.agent_label,
            "provider": "agent",
        })

    #: Batch-level, because a template is invisible one candidate at a time.
    frames = find_reused_frames([r["definition"] for r in rows])
    heavy = {g for g, n in frames.items() if n >= FRAME_REUSE_ROW_LIMIT}
    touched = [r for r in rows if _frames_in(r["definition"]) & frames.keys()]
    fraction = len(touched) / len(rows) if rows else 0.0
    batch_templated = (
        len(rows) >= FRAME_REUSE_BATCH_FLOOR
        and fraction > FRAME_REUSE_BATCH_FRACTION
    )

    if heavy or batch_templated:
        kept: list[dict[str, Any]] = []
        for row in rows:
            hit = _frames_in(row["definition"]) & (frames.keys() if batch_templated else heavy)
            if not hit:
                kept.append(row)
                continue
            phrase = max(hit, key=lambda g: frames[g])
            reason = ("definition_frame_reused" if not batch_templated
                      else "batch_is_templated")
            rejects[reason] += 1
            rejected.append({
                "reason": reason,
                "shared_frame": phrase,
                "shared_with": frames[phrase],
                "concept": str(row["concept"])[:120],
                "slot": str(row["maps_to_slot"])[:60],
                "confidence": row["confidence_in_literature"],
                "definition": str(row["definition"])[:300],
                "evidence": str(row["evidence_quote"])[:300],
                "chunk_index": None,
            })
        rows = kept

    rows, collapsed = deduplicate(rows)

    args.output_file.parent.mkdir(parents=True, exist_ok=True)
    args.output_file.write_text(json.dumps(rows, indent=2), encoding="utf-8")
    if args.rejects_file:
        args.rejects_file.parent.mkdir(parents=True, exist_ok=True)
        args.rejects_file.write_text(json.dumps(rejected, indent=2), encoding="utf-8")

    histogram = Counter(r["occurrences"] for r in rows)
    print(f"\n{args.source_book}  ({args.agent_label})")
    print(f"  candidates submitted    {len(candidates)}")
    print(f"  kept                    {len(rows)}  ({collapsed} duplicates merged)")
    print(f"  rejected                {sum(rejects.values())}  {dict(rejects.most_common())}")
    if frames:
        worst = sorted(frames.items(), key=lambda kv: -kv[1])[:3]
        verdict = "SUBMISSION REFUSED AS TEMPLATED" if batch_templated else "reported only"
        print(f"  reused frames           {len(frames)} distinct 8-grams, "
              f"{fraction:.0%} of rows touched  ({verdict})")
        for phrase, count in worst:
            print(f'      x{count:<5} "{phrase}"')
    print(f"  occurrence histogram    {dict(sorted(histogram.items()))}")
    print(f"  written                 {args.output_file}")
    if args.rejects_file:
        print(f"  rejects written         {args.rejects_file}")
    return 0


def _summarise(candidate: dict[str, Any]) -> dict[str, Any]:
    """Enough of a refused candidate to diagnose it. A count is not a reason."""
    return {
        "concept": str(candidate.get("concept", ""))[:120],
        "slot": str(candidate.get("slot", ""))[:60],
        "confidence": candidate.get("confidence"),
        "definition": str(candidate.get("definition", ""))[:300],
        "evidence": str(candidate.get("evidence", ""))[:300],
        "chunk_index": candidate.get("chunk_index"),
    }


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    sub = ap.add_subparsers(dest="command", required=True)

    d = sub.add_parser("chunks", help="write the chunks for an agent to read")
    d.add_argument("--input-file", required=True, type=pathlib.Path)
    d.add_argument("--output-file", required=True, type=pathlib.Path)
    d.set_defaults(func=dump_chunks)

    g = sub.add_parser("gate", help="run the gates over agent-written candidates")
    g.add_argument("--input-file", required=True, type=pathlib.Path)
    g.add_argument("--candidates", required=True, type=pathlib.Path)
    g.add_argument("--source-book", required=True)
    g.add_argument("--output-file", required=True, type=pathlib.Path)
    g.add_argument("--rejects-file", type=pathlib.Path)
    g.add_argument(
        "--agent-label", default="agent",
        help="what produced these candidates, recorded in every row so a "
             "later reader can tell agent output from provider output",
    )
    g.set_defaults(func=gate)

    args = ap.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
