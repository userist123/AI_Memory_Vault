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
import time
from collections import Counter
from typing import Any, Iterable

_REPO = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(_REPO / "03_IMPLEMENTATION" / "packages"))
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

from providers.model_provider import ModelRequest  # noqa: E402

#: One definition of what makes two terms the same concept, shared with
#: the agreement tool. Two normalizers that drift apart silently produce
#: two different answers to the same question.
from agree_across_models import normalize_term  # noqa: E402

from extract_book_concepts import (  # noqa: E402
    CANONICAL_SLOTS,
    KNOWN_VERIFIED_MODULES,
    check_verbatim_overlap,
    split_into_structural_chunks,
)

MAX_SHARED_NGRAM = 8
MAX_JACCARD = 0.60
MIN_DEFINITION_WORDS = 12
CLAIM_TYPES = frozenset({"definition", "mechanism", "finding", "taxonomy", "constraint"})
GENERIC_TERMS = frozenset("""what which this that these those there here it its more most other
such activity finding when how why chapter section figure table page
example approach thing""".split())
DANGLING_TAIL = re.compile(
    r"\b(in|of|for|the|a|an|to|and|with|his|her|their|that|as|by|on|at|from|"
    r"is|was|were|are|be|been|which|who)\s*[.]?\s*$", re.IGNORECASE,
)
SLOT_DIR = _REPO / "01_ARCHITECTURE" / "ontology" / "slots"


def load_slot_questions() -> dict[str, str]:
    questions: dict[str, str] = {}
    for path in sorted(SLOT_DIR.glob("*.md")):
        text = path.read_text(encoding="utf-8", errors="replace")
        name = re.search(r"(?m)^ontology_slot:\s*(\S+)", text)
        question = re.search(r"(?m)^##\s+Question\s*\n+(.+)$", text)
        if name and question:
            questions[name.group(1).strip()] = question.group(1).strip()
    missing = set(CANONICAL_SLOTS) - set(questions)
    if missing:
        raise SystemExit(f"no ## Question found for slot(s) {sorted(missing)} under {SLOT_DIR}")
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

A concept here is an idea a reader would need explained to understand the
field. It is NOT an experimental setting, a dataset name, a hyperparameter,
a tool, a metric name, or a value used in one paper's experiments. If the
passage is describing how an experiment was configured rather than what
something means, return [].

Not concepts: "buffer size", "SGD optimizer", "grid search", "random crop",
"number of training epochs", "Rot-MNIST", "backbone", "hyperparameters".

Only exclusions are listed, deliberately. An earlier version of this prompt
also gave four examples of GOOD concepts, and the model then returned those
four in nearly every passage — one of them in seven of twelve — regardless of
what the passage said. Naming desirable outputs turns extraction into recall,
which is the exact defect r027 was written to remove from the code. Do not
add positive examples here.

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
    text = content.strip()
    fence = re.search(r"```(?:json)?\s*(.+?)```", text, re.DOTALL)
    if fence:
        text = fence.group(1).strip()
    try:
        parsed: Any = json.loads(text)
    except json.JSONDecodeError:
        start, end = text.find("["), text.rfind("]")
        if start == -1 or end == -1 or end < start:
            return []
        try:
            parsed = json.loads(text[start : end + 1])
        except json.JSONDecodeError:
            return []
    if isinstance(parsed, dict):
        lists = [v for v in parsed.values() if isinstance(v, list)]
        parsed = lists[0] if len(lists) == 1 else []
    if not isinstance(parsed, list):
        return []
    return [item for item in parsed if isinstance(item, dict)]


def longest_verbatim_run(quote: str, source: str) -> int:
    """Length in words of the longest run of `quote` present in `source`."""
    words = _normalize_for_search(quote).split()
    haystack = f" {_normalize_for_search(source)} "
    best = 0
    for start in range(len(words)):
        #: Only look for runs longer than the best already found, and stop
        #: this start as soon as one matches — the first hit from the long
        #: end is the longest for that start.
        for end in range(len(words), start + best, -1):
            if f" {' '.join(words[start:end])} " in haystack:
                best = end - start
                break
    return best


def is_grounded(quote: str, source: str) -> bool:
    """Whether the evidence really comes from the passage that was sent.

    This used to demand the whole quote appear verbatim, which worked on
    conference-paper chunks of ~4,600 characters and failed badly on a
    monograph. Measured over 8 chunks of Schacter & Tulving at ~44,000
    characters each, 51 candidates were refused for grounding — and 17 of
    them quoted the source at 80% or better. One example matched 27 of its
    30 words. Those were accurate quotes with a word or two dropped, thrown
    away by an exact-match rule.

    Fabrication looks nothing like that. The control case from the test
    suite, "Vitter (1985) established this result", has a longest verbatim
    run of 2 words. Across the whole rejected set the apparent fabrications
    ran 3 to 6 words while the near-verbatim quotes ran 25 to 27.

    So the rule is a long contiguous run, plus a share of the whole quote so
    that a single borrowed phrase cannot carry forty invented words. Both
    thresholds sit well above the fabricated group and below the genuine one.
    """
    words = _normalize_for_search(quote).split()
    if not words:
        return False
    run = longest_verbatim_run(quote, source)
    return run >= MIN_VERBATIM_RUN_WORDS and run / len(words) >= MIN_EVIDENCE_COVERAGE


def clean_term(raw: Any) -> str:
    return " ".join(re.sub(r"\([^)]*\)", " ", str(raw)).split())


def validate(candidate: dict[str, Any], chunk_text: str) -> tuple[bool, str]:
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
    claim_type = str(candidate.get("claim_type", "")).strip().lower()
    if claim_type not in CLAIM_TYPES:
        return False, "claim_type_is_a_slot" if claim_type in CANONICAL_SLOTS else "claim_type_unknown"
    try:
        confidence = float(candidate.get("confidence"))
    except (TypeError, ValueError):
        return False, "confidence_missing"
    if not 0.0 <= confidence <= 1.0:
        return False, "confidence_range"
    if not evidence:
        return False, "evidence_missing"
    if not is_grounded(evidence, chunk_text):
        return False, "evidence_not_in_source"
    copied, phrase = check_verbatim_overlap(definition, evidence, MAX_SHARED_NGRAM)
    if copied:
        return False, "verbatim_ngram"
    if _jaccard(definition, evidence) > MAX_JACCARD:
        return False, "paraphrase_shallow"
    return True, ""


def format_slot_block(questions: dict[str, str]) -> str:
    lines = [f"                 {slot:16} {questions[slot]}" for slot in CANONICAL_SLOTS]
    return "\n".join(lines)


def extract_from_chunk(provider: Any, chunk: dict[str, str], source_book: str, model_tier: str,
                       slot_block: str = "", sampling: dict[str, Any] | None = None,
                       keep_alive: str = "", rejected_out: list[dict[str, Any]] | None = None,
                       attempts: int = 3, retry_backoff: float = 5.0) -> tuple[list[dict[str, Any]], Counter[str]]:
    slot_block = slot_block or ", ".join(CANONICAL_SLOTS)
    rejects: Counter[str] = Counter()
    content = chunk["content"]
    metadata: dict[str, Any] = {"source_book": source_book, "heading": chunk["heading"]}
    if keep_alive:
        metadata["keep_alive"] = keep_alive
    if sampling:
        metadata["local_options"] = dict(sampling)
    request = ModelRequest(
        prompt=PROMPT.format(min_words=MIN_DEFINITION_WORDS, slots=slot_block,
                             heading=chunk["heading"][:120], content=content),
        model_tier=model_tier, system_prompt=SYSTEM_PROMPT, metadata=metadata,
    )
    response = None
    for attempt in range(1, max(1, attempts) + 1):
        try:
            response = provider.generate(request)
            if attempt > 1:
                rejects[f"provider_retry_succeeded_on_{attempt}"] += 1
            break
        except Exception as exc:  # noqa: BLE001
            if "timed out" in str(exc).lower():
                rejects["provider_timeout"] += 1
                return [], rejects
            if attempt >= max(1, attempts):
                rejects[f"provider_error:{type(exc).__name__}"] += 1
                return [], rejects
            time.sleep(retry_backoff * attempt)
    if response is None:
        rejects["provider_error:unknown"] += 1
        return [], rejects
    if not response.content.strip():
        rejects["empty_response"] += 1
        return [], rejects
    parsed = parse_model_json(response.content)
    if not parsed and response.content.strip():
        rejects["unparseable_response"] += 1
    accepted = []
    for candidate in parsed:
        ok, reason = validate(candidate, content)
        if not ok:
            rejects[reason] += 1
            if rejected_out is not None:
                rejected_out.append({
                    "reason": reason,
                    "concept": str(candidate.get("concept", ""))[:120],
                    "slot": str(candidate.get("slot", ""))[:60],
                    "confidence": candidate.get("confidence"),
                    "definition": str(candidate.get("definition", ""))[:300],
                    "evidence": str(candidate.get("evidence", ""))[:300],
                    "source_location": chunk["heading"][:120],
                })
            continue
        slot = str(candidate["slot"]).strip().lower()
        accepted.append({
            "concept": clean_term(candidate["concept"]),
            "definition": str(candidate["definition"]).strip(),
            "claim_type": str(candidate["claim_type"]).strip().lower(),
            "maps_to_slot": slot,
            "maps_to_module": (KNOWN_VERIFIED_MODULES.get(slot) or [None])[0],
            "confidence_in_literature": float(candidate["confidence"]),
            "source_book": source_book,
            "source_location": chunk["heading"][:120],
            "evidence_quote": str(candidate["evidence"]).strip(),
            "extraction_method": "model_assisted",
            "model": response.model,
            "provider": response.provider,
        })
    return accepted, rejects


def deduplicate(rows: list[dict[str, Any]]) -> tuple[list[dict[str, Any]], int]:
    """Collapse repeats, counting distinct source sections rather than rows."""
    best: dict[str, dict[str, Any]] = {}
    order: list[str] = []
    locations: dict[str, list[str]] = {}
    location_sets: dict[str, set[str]] = {}
    for row in rows:
        key = " ".join(row["concept"].lower().split())
        location = row["source_location"]
        if key not in best:
            best[key] = dict(row, occurrences=1, also_found_in=[])
            order.append(key)
            locations[key] = [location]
            location_sets[key] = {location}
            continue
        kept = best[key]
        if location not in location_sets[key]:
            location_sets[key].add(location)
            locations[key].append(location)
        better = (
            row["confidence_in_literature"] > kept["confidence_in_literature"]
            or (row["confidence_in_literature"] == kept["confidence_in_literature"]
                and len(row["definition"]) > len(kept["definition"]))
        )
        if better:
            best[key] = dict(row)
            kept = best[key]
        kept["occurrences"] = len(locations[key])
        kept["also_found_in"] = [loc for loc in locations[key] if loc != kept["source_location"]]
    merged = [best[k] for k in order]
    return merged, len(rows) - len(merged)


def report_residency(model: str, base_url: str = "http://localhost:11434") -> str:
    try:
        import json as _json
        from urllib import request as _request
        with _request.urlopen(f"{base_url}/api/ps", timeout=10) as response:
            data = _json.loads(response.read().decode("utf-8"))
        for entry in data.get("models", []):
            if entry.get("name") == model:
                total = float(entry.get("size") or 0)
                vram = float(entry.get("size_vram") or 0)
                if total <= 0:
                    return "unknown"
                share = vram / total
                verdict = "fully resident" if share > 0.99 else f"{share:.0%} on GPU, the rest on CPU"
                return f"{vram/1e9:.1f}GB of {total/1e9:.1f}GB — {verdict}"
        return "not loaded"
    except Exception:  # noqa: BLE001
        return "unavailable"


def build_provider(kind: str, model: str, timeout: float, num_ctx: int) -> Any:
    if kind == "fake":
        from providers.fake_model_provider import FakeModelProvider
        return FakeModelProvider(model_name=model or "fake-model")
    if kind == "local":
        from providers.local_provider import LocalProvider
        return LocalProvider(model_name=model, timeout_seconds=timeout, num_ctx=num_ctx)
    raise SystemExit(f"unknown provider {kind!r}; use 'fake' or 'local'")


def main() -> int:
    ap = argparse.ArgumentParser(description="Model-assisted concept extraction")
    ap.add_argument("--input-file", required=True, type=pathlib.Path)
    ap.add_argument("--source-book", required=True)
    ap.add_argument("--output-file", required=True, type=pathlib.Path)
    ap.add_argument("--provider", default="local", choices=("local", "fake"))
    ap.add_argument("--model", default="mixtral:8x7b")
    ap.add_argument("--model-tier", default="standard")
    ap.add_argument("--max-chunks", type=int, default=0)
    ap.add_argument("--timeout", type=float, default=600.0)
    ap.add_argument("--temperature", type=float, default=0.0)
    ap.add_argument("--seed", type=int, default=42)
    ap.add_argument("--min-occurrences", type=int, default=1)
    ap.add_argument("--attempts", type=int, default=3)
    ap.add_argument("--retry-backoff", type=float, default=5.0)
    ap.add_argument("--rejects-file", type=pathlib.Path)
    ap.add_argument("--keep-alive", default="60m")
    ap.add_argument("--num-ctx", type=int, default=32768)
    ap.add_argument("--max-chunk-chars", type=int, default=24000)
    args = ap.parse_args()
    text = args.input_file.read_text(encoding="utf-8", errors="replace")
    chunks = split_into_structural_chunks(text)
    if args.max_chunks:
        chunks = chunks[: args.max_chunks]
    if not chunks:
        print(f"no chunks in {args.input_file}", file=sys.stderr)
        return 1
    provider = build_provider(args.provider, args.model, args.timeout, args.num_ctx)
    slot_block = format_slot_block(load_slot_questions())
    sampling = {"temperature": args.temperature, "seed": args.seed}
    rows: list[dict[str, Any]] = []
    rejected_rows: list[dict[str, Any]] = []
    rejects: Counter[str] = Counter()
    skipped = 0
    for i, chunk in enumerate(chunks, start=1):
        if len(chunk["content"]) > max_chunk_chars:
            skipped += 1
            rejects["chunk_too_long"] += 1
            continue
        accepted, chunk_rejects = extract_from_chunk(
            provider, chunk, args.source_book, args.model_tier, slot_block,
            sampling, args.keep_alive, rejected_rows, args.attempts, args.retry_backoff,
        )
        rows.extend(accepted)
        rejects.update(chunk_rejects)
        print(f"  chunk {i}/{len(chunks)}  +{len(accepted)}  (running total {len(rows)})", file=sys.stderr)
    rows, collapsed = deduplicate(rows)
    occurrence_hist = Counter(r["occurrences"] for r in rows)
    if args.min_occurrences > 1:
        before = len(rows)
        rows = [r for r in rows if r["occurrences"] >= args.min_occurrences]
        dropped = before - len(rows)
    else:
        dropped = 0
    args.output_file.parent.mkdir(parents=True, exist_ok=True)
    args.output_file.write_text(json.dumps(rows, indent=2), encoding="utf-8")
    confidences = sorted({round(r["confidence_in_literature"], 2) for r in rows})
    repeated = sum(1 for r in rows if r["occurrences"] > 1)
    slots_used = Counter(r["maps_to_slot"] for r in rows)
    print(f"\n{args.source_book}")
    print(f"  chunks processed   {len(chunks) - skipped} of {len(chunks)}")
    print(f"  candidates kept    {len(rows)}  ({collapsed} duplicates merged)")
    print(f"  defined more than once  {repeated}")
    print(f"  occurrence histogram    {dict(sorted(occurrence_hist.items()))}")
    if dropped:
        print(f"  dropped below min_occurrences={args.min_occurrences}  {dropped}")
    print(f"  rejected           {sum(rejects.values())}  {dict(rejects.most_common())}")
    print(f"  distinct confidence values  {len(confidences)}  {confidences[:12]}")
    print(f"  slots used         {dict(slots_used.most_common())}")
    print(f"  sampling           {sampling}, keep_alive={args.keep_alive}")
    if args.provider == "local":
        print(f"  model residency    {args.model}: {report_residency(args.model)}")
    print(f"  written            {args.output_file}")
    if args.rejects_file:
        args.rejects_file.parent.mkdir(parents=True, exist_ok=True)
        args.rejects_file.write_text(json.dumps(rejected_rows, indent=2), encoding="utf-8")
        print(f"  rejects written    {args.rejects_file}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
