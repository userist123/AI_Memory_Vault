#!/usr/bin/env python3
"""
model_extract_concepts.py — concept extraction that reads, rather than
pattern-matches.

## Why this exists

`extract_book_concepts.py` builds definitions by filling a template with a
fragment of the source sentence. On conference-paper prose that survives. On
a monograph it does not. Measured over the 18 converted books in
`06_INBOX/Carti`:

    112 candidates, of which 28% were unusable as a term
    ("What", "Chapter 7", "Holder Facts Deny Yer"),
    7% had definitions truncated mid-clause, and confidence took
    exactly two values across all 112.

    "Mental exercise defines a finding for Ramon y Cajal spelled out
     this idea in his Croonian."

That is not a paraphrase problem to be tuned. Producing a definition from a
paragraph of argument requires reading the paragraph, so this module asks a
model to do it.

## What it does NOT do

It does not trust the model. A model asked for definitions will happily
invent them, and an invented definition with a plausible citation is worse
than no definition — it is a provenance chain that reads as sound and is not.

Every candidate must survive gates the model cannot talk its way past:

  grounding   The evidence quote must appear verbatim in the chunk that was
              actually sent. This is checked against the text, not against
              the model's claim about the text. A candidate whose evidence
              cannot be located is dropped, which is what makes hallucinated
              citations non-viable rather than merely discouraged.

  paraphrase  Two floors, both required, per the r029 finding that the
              15-word guard is a floor and not a quality bar: no shared
              n-gram of MAX_SHARED_NGRAM words with the evidence, and token
              overlap below MAX_JACCARD. A synonym-swap of the source
              sentence fails the second even when it passes the first.

  shape       Terms and definitions that are structurally wrong — a
              one-word generic, a definition that stops mid-clause — are
              rejected rather than repaired. Repairing them is how the
              previous extractor produced its 28%.

  slot        Must name one of the sixteen canonical slots. The model does
              not get to invent an ontology.

Rejections are counted by reason and reported. A run that accepts 6 of 40
says so; the number is the finding, not something to be tuned away.

## Provider

Depends only on the `ModelProvider` Protocol — never on a concrete provider.
`FakeModelProvider` drives the tests with no network. `LocalProvider` talks
to a local Ollama endpoint, which needs no API key, no cloud account and no
SDK. Nothing here imports a vendor library or makes a network call itself.
"""

from __future__ import annotations

import argparse
import json
import pathlib
import re
import sys
from collections import Counter
from typing import Any, Iterable

_REPO = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(_REPO / "03_IMPLEMENTATION" / "packages"))
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

from providers.model_provider import ModelRequest  # noqa: E402

from extract_book_concepts import (  # noqa: E402
    CANONICAL_SLOTS,
    KNOWN_VERIFIED_MODULES,
    check_verbatim_overlap,
    split_into_structural_chunks,
)

#: Tighter than the inherited 15-word guard. r029 recorded promoted notes
#: passing that guard while still carrying 10- and 11-word verbatim runs.
MAX_SHARED_NGRAM = 8

#: Token overlap between a definition and its evidence. A genuine
#: restatement of a sentence shares its content words and little else;
#: measured on the r028 promotion, a synonym-swap scored 0.68-0.72, so the
#: ceiling sits below that.
MAX_JACCARD = 0.60

#: A definition shorter than this is a label, not a definition.
MIN_DEFINITION_WORDS = 12

#: Terms that are grammar rather than concepts. Every one of these was
#: actually emitted as a concept by the rule-based extractor.
GENERIC_TERMS = frozenset(
    """what which this that these those there here it its more most other
    such activity finding when how why chapter section figure table page
    example approach thing""".split()
)

#: A definition that ends on one of these stopped mid-clause.
DANGLING_TAIL = re.compile(
    r"\b(in|of|for|the|a|an|to|and|with|his|her|their|that|as|by|on|at|from|"
    r"is|was|were|are|be|been|which|who)\s*[.]?\s*$",
    re.IGNORECASE,
)

#: Where the slot definitions live. They are read at run time rather than
#: copied here: the vault is the authority on its own ontology, and a
#: hardcoded copy is a second definition that silently goes stale.
SLOT_DIR = _REPO / "01_ARCHITECTURE" / "ontology" / "slots"


def load_slot_questions() -> dict[str, str]:
    """Each canonical slot mapped to the question it answers.

    Slot NAMES alone do not disambiguate — asked to place "synaptic
    consolidation" against a bare list, the model chose `procedures` over
    `consolidation`, and "catastrophic forgetting" over `state`. The slot
    files carry a one-line `## Question` each ("How does experience become
    knowledge?"), which is the actual selection criterion.
    """
    questions: dict[str, str] = {}
    for path in sorted(SLOT_DIR.glob("*.md")):
        text = path.read_text(encoding="utf-8", errors="replace")
        name = re.search(r"(?m)^ontology_slot:\s*(\S+)", text)
        question = re.search(r"(?m)^##\s+Question\s*\n+(.+)$", text)
        if name and question:
            questions[name.group(1).strip()] = question.group(1).strip()

    missing = set(CANONICAL_SLOTS) - set(questions)
    if missing:
        #: Loudly, because a silently missing slot becomes a slot the model
        #: is never offered and therefore never selects.
        raise SystemExit(
            f"no ## Question found for slot(s) {sorted(missing)} under {SLOT_DIR}"
        )
    return questions


SYSTEM_PROMPT = (
    "You extract load-bearing technical concepts from academic text for a "
    "knowledge base. You are precise and you never invent. If a passage "
    "contains no definable concept, you return an empty list, and that is a "
    "correct answer rather than a failure."
)

PROMPT = """\
Read the passage below and identify the technical concepts it DEFINES or
EXPLAINS. Ignore concepts it merely mentions in passing.

Return JSON only — a list of objects, no prose around it. Each object:

  "concept"    The term, 1-4 words, as a noun phrase. Never a pronoun, a
               section label, or a sentence fragment.
  "definition" What the passage says this term means, in YOUR OWN WORDS.
               At least {min_words} words, a complete sentence. Do not reuse
               the passage's phrasing — a reader must be able to understand
               the term from your sentence alone.
  "evidence"   One sentence copied EXACTLY from the passage, character for
               character, that supports the definition. It must be present
               in the passage verbatim.
  "claim_type" One of: definition, mechanism, finding, taxonomy, constraint.
  "slot"       The single best fit. Choose by which QUESTION the concept
               helps answer, not by which name sounds closest:
{slots}
  "confidence" Your confidence from 0.0 to 1.0 that this is a real, load-
               bearing concept the passage genuinely defines. Use the full
               range. Be honest when you are unsure.

Return [] if the passage defines nothing. Do not pad the list.

PASSAGE ({heading}):
\"\"\"
{content}
\"\"\"
"""


def _tokens(text: str) -> list[str]:
    return [w for w in re.sub(r"[^\w\s]", " ", text.lower()).split() if w]


def _jaccard(a: str, b: str) -> float:
    sa, sb = set(_tokens(a)), set(_tokens(b))
    if not sa or not sb:
        return 0.0
    return len(sa & sb) / len(sa | sb)


def _normalize_for_search(text: str) -> str:
    return " ".join(_tokens(text))


def parse_model_json(content: str) -> list[dict[str, Any]]:
    """Pull the JSON list out of a model response.

    Tolerant of a fenced code block or a sentence of preamble, because those
    are formatting noise. NOT tolerant of malformed JSON: a response that
    cannot be parsed is a failed call, and guessing at its intent is how
    fabricated fields get in.
    """
    text = content.strip()
    fence = re.search(r"```(?:json)?\s*(.+?)```", text, re.DOTALL)
    if fence:
        text = fence.group(1).strip()
    start, end = text.find("["), text.rfind("]")
    if start == -1 or end == -1 or end < start:
        return []
    try:
        parsed = json.loads(text[start : end + 1])
    except json.JSONDecodeError:
        return []
    return [item for item in parsed if isinstance(item, dict)]


def clean_term(raw: Any) -> str:
    """The term without its acronym gloss.

    "Continual learning (CL)" is how academic prose introduces a term, and
    rejecting it on the parenthesis threw away real concepts — measured on a
    live run, where it was the first candidate the model returned.
    """
    return re.sub(r"\s*\([^)]*\)\s*$", "", str(raw)).strip()


def validate(candidate: dict[str, Any], chunk_text: str) -> tuple[bool, str]:
    """Whether this candidate may be kept, and if not, why not."""
    term = clean_term(candidate.get("concept", ""))
    definition = str(candidate.get("definition", "")).strip()
    evidence = str(candidate.get("evidence", "")).strip()
    slot = str(candidate.get("slot", "")).strip().lower()

    words = term.split()
    if not (1 <= len(words) <= 4):
        return False, "term_length"
    if not all(re.fullmatch(r"[A-Za-z][A-Za-z\-']*", w) for w in words):
        return False, "term_shape"
    if words[0].lower() in GENERIC_TERMS:
        return False, "term_generic"

    if len(definition.split()) < MIN_DEFINITION_WORDS:
        return False, "definition_short"
    if not definition.endswith((".", "!", "?")):
        return False, "definition_unterminated"
    if DANGLING_TAIL.search(definition):
        return False, "definition_truncated"

    if slot not in CANONICAL_SLOTS:
        return False, "slot_unknown"

    try:
        confidence = float(candidate.get("confidence"))
    except (TypeError, ValueError):
        return False, "confidence_missing"
    if not 0.0 <= confidence <= 1.0:
        return False, "confidence_range"

    #: Grounding, checked against the text rather than against the claim.
    if not evidence:
        return False, "evidence_missing"
    if _normalize_for_search(evidence) not in _normalize_for_search(chunk_text):
        return False, "evidence_not_in_source"

    #: Paraphrase floors, both required.
    copied, phrase = check_verbatim_overlap(definition, evidence, MAX_SHARED_NGRAM)
    if copied:
        return False, "verbatim_ngram"
    if _jaccard(definition, evidence) > MAX_JACCARD:
        return False, "paraphrase_shallow"

    return True, ""


def format_slot_block(questions: dict[str, str]) -> str:
    lines = [
        f"                 {slot:16} {questions[slot]}" for slot in CANONICAL_SLOTS
    ]
    return "\n".join(lines)


def extract_from_chunk(
    provider: Any,
    chunk: dict[str, str],
    source_book: str,
    model_tier: str,
    slot_block: str = "",
) -> tuple[list[dict[str, Any]], Counter[str]]:
    """One model call for one chunk. Returns accepted rows and reject counts."""
    slot_block = slot_block or ", ".join(CANONICAL_SLOTS)
    rejects: Counter[str] = Counter()
    content = chunk["content"]

    request = ModelRequest(
        prompt=PROMPT.format(
            min_words=MIN_DEFINITION_WORDS,
            slots=slot_block,
            heading=chunk["heading"][:120],
            content=content,
        ),
        model_tier=model_tier,
        system_prompt=SYSTEM_PROMPT,
        metadata={"source_book": source_book, "heading": chunk["heading"]},
    )

    try:
        response = provider.generate(request)
    except Exception as exc:  # provider failures are data, not a crash
        rejects[f"provider_error:{type(exc).__name__}"] += 1
        return [], rejects

    accepted = []
    for candidate in parse_model_json(response.content):
        ok, reason = validate(candidate, content)
        if not ok:
            rejects[reason] += 1
            continue
        slot = str(candidate["slot"]).strip().lower()
        accepted.append(
            {
                "concept": clean_term(candidate["concept"]),
                "definition": str(candidate["definition"]).strip(),
                "claim_type": str(candidate.get("claim_type", "definition")).strip(),
                "maps_to_slot": slot,
                "maps_to_module": (KNOWN_VERIFIED_MODULES.get(slot) or [None])[0],
                "confidence_in_literature": float(candidate["confidence"]),
                "source_book": source_book,
                "source_location": chunk["heading"][:120],
                #: Kept so a reviewer can check the definition against the
                #: sentence it came from without reopening the book.
                "evidence_quote": str(candidate["evidence"]).strip(),
                "extraction_method": "model_assisted",
                "model": response.model,
                "provider": response.provider,
            }
        )
    return accepted, rejects


def deduplicate(rows: list[dict[str, Any]]) -> tuple[list[dict[str, Any]], int]:
    """Collapse repeats of the same term, keeping the best-evidenced one.

    A book returns to its central ideas, so the same concept is defined more
    than once. Measured on three chunks of one paper: "Synaptic
    consolidation" three times, "Episodic Memory" twice. Left alone those
    become three slot rows for one concept and three review decisions.

    The count of distinct sections defining a term is kept as `occurrences`,
    because unlike the model's self-reported confidence it is an OBSERVED
    signal: a concept a book defines in four places is load-bearing in a way
    that a concept mentioned once is not.
    """
    best: dict[str, dict[str, Any]] = {}
    order: list[str] = []
    for row in rows:
        key = " ".join(row["concept"].lower().split())
        if key not in best:
            best[key] = dict(row, occurrences=1, also_found_in=[])
            order.append(key)
            continue

        kept = best[key]
        kept["occurrences"] += 1
        location = row["source_location"]
        if location != kept["source_location"] and location not in kept["also_found_in"]:
            kept["also_found_in"].append(location)

        #: Prefer the more confident definition; on a tie, the longer one,
        #: which in practice is the one that actually explains the term.
        better = (
            row["confidence_in_literature"] > kept["confidence_in_literature"]
            or (
                row["confidence_in_literature"] == kept["confidence_in_literature"]
                and len(row["definition"]) > len(kept["definition"])
            )
        )
        if better:
            carried = {
                "occurrences": kept["occurrences"],
                "also_found_in": kept["also_found_in"],
            }
            best[key] = dict(row, **carried)

    merged = [best[k] for k in order]
    return merged, len(rows) - len(merged)


def build_provider(kind: str, model: str, timeout: float, num_ctx: int) -> Any:
    if kind == "fake":
        from providers.fake_model_provider import FakeModelProvider

        return FakeModelProvider(model_name=model or "fake-model")
    if kind == "local":
        from providers.local_provider import LocalProvider

        #: LocalProvider defaults to 120s, which a large model exceeds on a
        #: book-sized chunk — measured: every call against a 26B model
        #: failed at exactly 120s and was recorded as a provider error, which
        #: reads as "the model found nothing" unless the timeout is visible.
        return LocalProvider(
            model_name=model, timeout_seconds=timeout, num_ctx=num_ctx
        )
    raise SystemExit(f"unknown provider {kind!r}; use 'fake' or 'local'")


def main() -> int:
    ap = argparse.ArgumentParser(description="Model-assisted concept extraction")
    ap.add_argument("--input-file", required=True, type=pathlib.Path)
    ap.add_argument("--source-book", required=True)
    ap.add_argument("--output-file", required=True, type=pathlib.Path)
    ap.add_argument("--provider", default="local", choices=("local", "fake"))
    ap.add_argument("--model", default="gemma4:26b-64k")
    ap.add_argument("--model-tier", default="standard")
    ap.add_argument(
        "--max-chunks", type=int, default=0,
        help="stop after this many chunks (0 = all); use it to measure before "
             "committing to a full book",
    )
    ap.add_argument(
        "--timeout", type=float, default=600.0,
        help="seconds per model call (default 600; the provider default of "
             "120 is not enough for a large model on a book chunk)",
    )
    ap.add_argument(
        "--num-ctx", type=int, default=32768,
        help="context window requested from the local model",
    )
    ap.add_argument(
        "--max-chunk-chars", type=int, default=24000,
        help="chunks longer than this are skipped rather than silently "
             "truncated by the model's context window",
    )
    args = ap.parse_args()

    text = args.input_file.read_text(encoding="utf-8", errors="replace")
    chunks = split_into_structural_chunks(text)
    if args.max_chunks:
        chunks = chunks[: args.max_chunks]
    if not chunks:
        print(f"no chunks in {args.input_file}", file=sys.stderr)
        return 1

    provider = build_provider(
        args.provider, args.model, args.timeout, args.num_ctx
    )
    slot_block = format_slot_block(load_slot_questions())

    rows: list[dict[str, Any]] = []
    rejects: Counter[str] = Counter()
    skipped = 0
    for i, chunk in enumerate(chunks, start=1):
        if len(chunk["content"]) > args.max_chunk_chars:
            skipped += 1
            rejects["chunk_too_long"] += 1
            continue
        accepted, chunk_rejects = extract_from_chunk(
            provider, chunk, args.source_book, args.model_tier, slot_block
        )
        rows.extend(accepted)
        rejects.update(chunk_rejects)
        print(
            f"  chunk {i}/{len(chunks)}  +{len(accepted)}  "
            f"(running total {len(rows)})",
            file=sys.stderr,
        )

    args.output_file.parent.mkdir(parents=True, exist_ok=True)
    args.output_file.write_text(json.dumps(rows, indent=2), encoding="utf-8")

    confidences = sorted({round(r["confidence_in_literature"], 2) for r in rows})
    print(f"\n{args.source_book}")
    print(f"  chunks processed   {len(chunks) - skipped} of {len(chunks)}")
    print(f"  candidates kept    {len(rows)}")
    print(f"  rejected           {sum(rejects.values())}  {dict(rejects.most_common())}")
    print(f"  distinct confidence values  {len(confidences)}  {confidences[:12]}")
    print(f"  written            {args.output_file}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
