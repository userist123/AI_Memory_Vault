# Book corpus conversion — measured findings

Package: `r030/pdf-text-frontend`
Measured: 2026-09-07, against `06_INBOX/Carti` (20 PDFs, 151 MB)
Status: conversion works; **extraction at book scale does not**

The PDFs themselves are not committed (`06_INBOX/*` is gitignored, and these
are copyrighted works). Neither is the text extracted from them. What is
committed is the script that produces it and the numbers it produced.

## What was missing

`extract_book_concepts.py` documents itself as ingesting "plain text converted
from PDF/epub" and never says who converts it. Nothing in the repository did.
The single `.txt` in the corpus was made outside it, so nineteen books had no
path in. `30_SCRIPTS/ingestion/convert_pdf_to_text.py` is that step.

## Corpus

| | books |
|---|---|
| PDFs present | 20 |
| converted | 18 |
| **scans with no text layer — need OCR** | **2** |
| carrying an embedded outline (`toc` mode) | 5 |
| structure recovered from typography (`font`) | 5 |
| no recoverable structure (`pages` fallback) | 8 |

Total extracted: 6,612,146 characters → 1,107 chunks.

The two scans are Ashby's *Introduction to Cybernetics* (156 pp) and Minsky's
*Society of Mind* (336 pp). They extract to 0.0 characters per page. They are
refused rather than written, because a book that ingests to zero concepts and
a book with no text layer look identical downstream.

Two files in the corpus are not books at all: `7688_jkt_au.pdf` is a one-page
dust jacket, and `ilide.info-comparison-of-cognitive-architectures` is three
pages. Both convert; neither yields anything.

## The yield question, answered

The concern going in was volume — that if a 68 KB paper yields a dozen
candidates, 6.6 MB of monographs yields thousands, and a review queue nobody
can work through is backlog rather than memory.

That is not what happens. **112 candidates from 18 books.** The queue is not
the problem.

The problem is what is in it:

| | of 112 |
|---|---|
| unusable as a concept term | 31 (28%) |
| term begins with a generic word (`What`, `Activity`, `Chapter`) | 17 (15%) |
| definition truncated mid-clause | 8 (7%) |
| assigned a slot | 112 (100%) |
| distinct confidence values across all 112 | **2** (0.85, 0.9) |

Real terms produced: `What`, `Activity`, `He S 3 Mrs A`, `Holder Facts Deny
Yer`, `Dna That`, `Chapter 7`, `Hippocampus Rapidly`.

Real definitions produced:

> "Mental exercise defines a finding for Ramon y Cajal spelled out this idea
> in his Croonian."

> "What avert there simply being more and more levels of the cognitive band?"

The second carries the subject-verb break already recorded as defect D2 in the
r029 brief — `prevents` → `avert` — now reproduced at corpus scale.

Confidence is not a measurement. Every candidate scores 0.85 or 0.9, `What`
included.

`Holder Facts Deny Yer` is OCR damage promoted into a concept term, which is
the mechanism by which a degraded page becomes a knowledge claim.

## Why the tests did not catch this

`pytest 20_TESTS/test_book_ingestion_pipeline.py test_genuine_extraction.py
test_concept_promotion.py test_ontology_scaffold.py` → **18 passed**, both
before and after this work. The suite exercises the paper-scale path. Nothing
in it reads a monograph, so nothing in it fails when the extractor produces
`He S 3 Mrs A` from one.

## A defect found in this package's own first draft

The converter's first version reported `18/20 converted, 6,636,779
characters` and was wrong. `_normalize()` collapsed newlines across the whole
document with one regex, which merged every heading into the paragraph below
it. The consumer's heading pattern is `^#+\s+.*`, and `.*` stops at a
newline — so the match swallowed the entire section body, left `content`
empty, and the section was dropped by its `len(content) > 10` check.

Schacter & Tulving normalized to 1.18 MB across 57 lines and produced **zero
chunks**. Seven other books did the same. The conversion reported OK for all
of them.

This is the same shape as the defects this lineage keeps producing: a green
number standing in front of no work. It is fixed, and the fix is why the
corpus now yields 1,107 chunks instead of a few hundred empty ones.

## Three quality metrics that were tried and failed

Detecting a *corrupt* text layer, as opposed to an absent one, was attempted
three ways at document level:

| metric | Newell (visibly corrupt) | sarfraz22a (clean) |
|---|---|---|
| function-word rate | 41.5% | 31.3% |
| intra-word punctuation | 0.15% | 0.24% |
| low-vowel word rate | 0.75% | 1.39% |

All three rank the corrupt book as *cleaner* than the clean one. The reason is
that the corruption is local: Newell's front matter is wrecked
("Copyrighted Material Copyright © 1990 by the President and Fellows of
Harvard College" reads as "Copynyhrnd Moterrol Copyoght © IP© hr the
Prarident and Fnilotor at Honord Collage") while the body is largely intact,
and ten bad pages vanish in the average of a 200,000-word book.

No document-level detector is shipped. The check runs **per page**, and
Newell reports 14 degraded pages — which is the front matter, correctly
located.

## Known limits of what was built

- Heading detection from typography is guarded by a plausibility band of
  0.05–2.0 headings per page. This catches the extremes: Ashby produced 22
  headings per page and *How Can the Human Mind Occur* produced 0.03, and
  both now fall back to page grouping. **It does not catch Newell**, which
  sits inside the band at 1.63 per page while still marking body paragraphs
  as headings. For Newell the signal that fires is the degraded-page count,
  not the heading band.
- Eight of eighteen books fall back to `pages` mode. Their chunk boundaries
  are arbitrary and should be treated as lower-confidence input.
- `sarfraz22a.txt`, the externally produced normalized text that every prior
  package in this lineage was measured against, **was overwritten** by this
  run. It was gitignored and is not recoverable from git. Under the new
  converter the same paper yields 5 candidates where the old text yielded 2;
  the chunking differs (12 chunks via `toc` versus per-page), so prior yield
  numbers for this paper are not directly comparable to new ones.

## What this means for the plan

r031 was written to gate the batch on volume. That gate is answered and is
not where the risk was. The batch should instead be gated on **candidate
quality**, because the extractor's definitional heuristics were tuned on
conference-paper prose and do not survive contact with a monograph.

Ingesting the remaining books through the current extractor would add roughly
a hundred rows of which a quarter are unusable on their face, and the rest
carry definitions no reader would accept — with a confidence score that says
0.85 regardless.

---

# r031 — model-assisted extraction

Decided after the measurements above: the rule-based extractor is not tuned
into working on monograph prose, it is replaced for that job.

## Corpus decision

`RelațiiGraph+Judecată` is empty and stays empty. The theme is already
covered by the books under `Ontologie+Memorie-episodică-semantică-procedurală`
and `Memorie procedurală+Judecată+Routing`. It is dropped from the plan as a
separate theme and no files are moved into it. The corpus is 20 files in five
themes, not six.

## How it is wired

`30_SCRIPTS/ingestion/model_extract_concepts.py` depends only on the
provider-neutral `ModelProvider` Protocol. Tests run against
`FakeModelProvider` with no network; real runs use `LocalProvider` against a
local Ollama endpoint — no API key, no cloud account, no vendor SDK. The
script's original note about avoiding "external LLM API dependencies" is
honoured: nothing leaves the machine.

Note for whoever reads CLAUDE.md next: its protected-core paths
(`cognitive_core/model_provider.py` and siblings) do not exist. The modules
are in `03_IMPLEMENTATION/packages/providers/`.

## First real measurement

Three chunks of `sarfraz22a`, `glm-4.7-flash` via Ollama:

| | |
|---|---|
| candidates kept | 13 |
| rejected | 4 |
| — evidence not present in the source | 2 |
| — definition too short | 1 |
| — paraphrase too close to source | 1 |

The rule-based extractor produced 5 from the whole book. More importantly,
the definitions are usable:

> **Catastrophic forgetting** — A degradation in model performance on
> previously learned tasks that occurs when the model learns new tasks.

against the same pipeline's previous output:

> "Mental exercise defines a finding for Ramon y Cajal spelled out this idea
> in his Croonian."

**The two rejected fabrications are the important number.** The model
produced evidence quotes that were not in the passage it was given, and the
grounding check caught both. That check runs against the text actually sent,
not against the model's claim about it.

## Three defects in this package, unfixed and reported

1. **No deduplication.** Three chunks produced "Synaptic consolidation"
   three times and "Episodic Memory" twice. Dedup is needed within a book
   before it is needed across books.
2. **Confidence is still not calibrated.** Values were 0.9, 0.95 and 1.0 —
   three distinct values instead of the previous two, which is better and
   still not a measurement. The model is asked for the full range and does
   not use it.
3. **Slot assignment is unreliable.** "Synaptic consolidation" was routed to
   `procedures` rather than `consolidation`; "Catastrophic forgetting" to
   `state`. The slot gate checks that a slot is canonical, not that it is
   correct.

## Cost

Roughly 2.6 minutes per chunk on this machine. At 1,107 chunks the full
corpus is on the order of 48 hours of local inference. That is a scheduling
constraint, not a blocker, but it rules out iterating on the whole corpus —
tune on a few chunks, then run once.

## A known gap, recorded as a passing test

`test_known_gap_heavy_rewording_passes_both_paraphrase_floors` asserts that a
definition which rewords every content word while keeping the clause
structure scores 0.43 token overlap, clears both floors, and is accepted.
Neither gate can see structure, only vocabulary. The ceiling is not lowered
to catch it, because 0.60 is set by the measured 0.68-0.72 defect and
tightening further would reject genuine definitions that legitimately reuse
technical terminology.

## r031 follow-up — dedup, slot questions, and a variance finding

Three of the defects reported above were addressed. One of them cannot yet be
shown to have worked, and that is the most useful result in this section.

**Deduplication.** Repeats of a term collapse into one row, keeping the more
confident definition. The count of distinct sections defining the term is
kept as `occurrences`: unlike the model's self-reported confidence it is an
OBSERVED signal, and a concept a book defines in four places is load-bearing
in a way one mentioned once is not.

**Slot questions.** The prompt now carries each slot's `## Question`, read
from the slot files at run time rather than copied into the script — the
vault stays the authority on its own ontology, and a copy would go stale
silently. `load_slot_questions()` fails loudly if any canonical slot has lost
its question, because a slot the model is never offered is a slot it never
selects.

**An acronym gloss is no longer a rejection.** On a live run the shape check
threw away the first candidate the model returned, "Continual learning (CL)",
because of the parenthesis. That is how academic prose introduces a term. The
gloss is stripped and the term kept.

### The variance finding

Two runs of the identical three chunks produced **13 candidates and then 2**.
The second run also logged two provider errors and one unknown slot.

That spread is larger than the effect the slot-question change was meant to
have, so **the drop cannot be attributed to the change** — and neither could
an improvement have been. A single run of a sampling model is not a
measurement.

Anything claiming that a prompt change improved extraction on this pipeline
needs repeated runs at fixed settings, and at roughly four minutes per chunk
that is expensive enough to plan for rather than assume. Nothing here claims
slot routing improved; the mechanism is in place and its effect is unmeasured.

### Still open

- Confidence remains uncalibrated. Observed values across runs: 0.9, 0.95,
  1.0 — and in the second run a single distinct value. Asking for the full
  range does not produce it.
- Slot correctness is unverified, per the variance finding above.
- Cross-book deduplication is not done. Within-book is.
