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

## r031 — the noise floor, measured

The previous section reported that the same three chunks yielded 13
candidates and then 2, and that nothing could be concluded from a single run.
That is now resolved, and the answer is not the one that was expected.

### Sampling is pinned through the provider's own escape hatch

`temperature: 0` and a fixed `seed` go through `metadata["local_options"]`,
which `LocalProvider` already supports. `ModelRequest` stays provider-neutral;
no Ollama-specific field was added to the shared contract, and nothing in the
protected core was touched.

### The result: two stable regimes, not noise

One chunk of `sarfraz22a`, five runs, identical settings throughout:

| run | model state | candidates | terms |
|---|---|---|---|
| 1 | cold (first load) | 5 | Semantic memory, Episodic memory, Instance-based hippocampal system, Parametric neocortical system, Plasticity and stability |
| 2 | warm | 6 | SYNERgy method, Continual learning, Experience replay, Dual memory experience replay, Episodic memory, Rehearsal-based approaches |
| 3 | warm | 6 | *identical to run 2* |
| 4 | warm | 6 | *identical to run 2* |
| 5 | cold (forced unload) | 5 | *identical to run 1* |

Runs 2-4 match byte for byte — same terms, same slots, same confidences, and
the same seven rejections with the same breakdown. Runs 1 and 5 match each
other.

So the model is deterministic in **both** states. This is not warm-up noise
that settles: it is two stable regimes, and load state selects between them.

**Consequence for the long run.** Ollama unloads an idle model by default. A
multi-hour ingestion can therefore cross a regime boundary partway through
and extract the second half of a corpus differently from the first, with
nothing in the output to indicate it. `--keep-alive` now defaults to 60m to
hold the model resident, and the setting is printed with every run.

**Consequence for comparisons.** Pinning the seed is necessary and not
sufficient. Any A/B between two prompts must also control load state, or it
measures the regime rather than the change — and with 5 vs 6 candidates
between regimes, that difference is the same size as the effects being
looked for.

### `format: "json"` is measured broken on this model

Ollama's structured-output flag looks like the correct way to guarantee
parseable responses. Against `glm-4.7-flash` it returns an empty string in
under a second, every time, while the identical request without it answers
normally in 27s:

| configuration | time | response |
|---|---|---|
| baseline | 27s | ` ```json [{"a": 1}] ``` ` |
| `format: json` | 0s | *empty* |
| `temperature` + `seed` | 6s | `[{"a": 1}]` |
| both | 0s | *empty* |

It is not set. Pinned sampling alone already produces clean JSON without
fences.

### The defect this exposed in our own code

The first three pinned runs reported `0 candidates, 0 rejected` — which reads
as three passages that happened to define nothing, not as a provider
returning nothing at all. An empty response and an empty result were the same
number.

`empty_response` and `unparseable_response` are now counted separately, each
with a test. This is the same failure shape as the `_normalize()` defect in
r030: a legitimate-looking zero standing in front of no work.

### Still open

- Confidence remains uncalibrated.
- `slot_unknown` fires exactly once in every warm run — a repeatable pattern,
  not an accident. The model proposes a slot outside the ontology for the
  same passage each time. Worth looking at, now that "the same each time" is
  a statement that can be made.
- Cross-book deduplication is still not done.

## r031 — a correction, and what the rejections say

### Correction: deduplication was reported working and was not

The previous section and its commit describe deduplication collapsing
repeated terms. That was false when written. `deduplicate()` shipped with
passing unit tests and **no caller** — the edits wiring it into `main()`
failed to apply silently, because `str.replace` says nothing when its anchor
does not match.

The evidence was visible and went unread: a run two steps earlier did not
print the `sampling` line that the same batch of edits was supposed to add.

It is wired now, verified by running it rather than by a green test:

```
candidates kept    8  (1 duplicates merged)
defined more than once  1
```

`test_main_actually_deduplicates_and_writes_rejects` drives `main()`
end to end over two identical sections and asserts against the written file.
A unit test on a function with no caller measures nothing — which is exactly
what the repository's own production-consumer rule says, applied here to our
own code.

### Model confidence is not merely uncalibrated, it is empty

One run, 18 candidates through the gates:

| | confidence |
|---|---|
| 8 accepted | 1.0 |
| 10 rejected | 1.0 |

Every candidate, including three whose evidence quote was not in the source
at all, is 1.0. Distinct values across the run: **one**. Asking for the full
range, twice, in the prompt, changes nothing.

`confidence_in_literature` should not be read as a signal by anything
downstream. `occurrences` — how many distinct sections define the term — is
the field that carries information, because it is counted rather than
claimed.

### What the rejections diagnose

Now that rejected candidates are written out with the offending value, not
just counted:

| reason | n | what it means |
|---|---|---|
| verbatim_ngram | 5 | the model reuses the source's phrasing; the top failure |
| evidence_not_in_source | 3 | fabricated quotes, still being caught |
| term_length | 1 | ours, not the model's — see below |
| definition_short | 1 | |

`Complementary Learning Systems (CLS) theory` was refused for length because
`clean_term()` stripped only a trailing gloss, leaving five words where four
are allowed. The gloss is now removed wherever it appears. That was our
defect rejecting a real concept.

### Slot routing is skewed, and the questions did not fix it

Slots chosen across 8 accepted candidates:

    procedures 5, map 1, constraints 1, identity 1

`procedures` asks "How are operations executed?", and a neuroscience
mechanism reads as an operation, so mechanisms land there regardless of
subject. Adding the slot questions to the prompt has not corrected this.
Nothing here claims it did.

`slot_unknown` did not fire in this run, so the earlier observation that it
fires exactly once per warm run remains unconfirmed under this
configuration — different chunk count, different regime. It is not carried
forward as established.

## r031 — retries, and what survives the merge boundary

### Provider failures are transient, and losing a chunk is silent

One run lost both its chunks to `LocalProviderError`. The identical request,
repeated by hand, succeeded in 148 seconds. The failure is transient, not
deterministic.

Unretried, that is a hole in the extraction with nothing in the output
pointing at it: the run reports a candidate count, and the count is simply
lower than it should be. Over a corpus of 1,107 chunks that is not a rare
event to be tolerated, it is an unknown fraction of the corpus quietly
missing.

`--attempts` defaults to 3 with an increasing backoff. A retry that succeeds
is counted under its own key, so the rate is visible rather than hidden. A
chunk that fails all attempts is still recorded and the run continues — one
unreachable chunk must not end a multi-hour job.

Adding this made the test suite take 15 seconds instead of 0.06, because an
older test slept the real backoff. Patched: a slow suite is a suite that
stops being run.

### The integration works, and drops almost everything

Verified end to end against a COPY of the slot files
(`--slots-dir` pointing at a temporary directory, vault untouched and
confirmed clean with `git status`). All 8 candidates merged into their
correct slot files.

What arrives in the ontology:

```
| Synaptic Consolidation | sarfraz | 1.00 | proposed | 2026-09-08 | |
```

The candidate row has six fixed columns, and none of them is the definition,
the evidence quote, or `occurrences`.

That matters more than it looks:

- The **evidence quote is the anti-hallucination mechanism**. It is checked
  against the source text, it is why three fabricated citations were caught
  in a single run — and it does not reach the person who reviews the row.
- **`occurrences` is the only field carrying real information**, since it is
  counted rather than claimed, and it is dropped.
- **`confidence` is written as `1.00` on every row.** It is the one thing
  that does survive, it is meaningless, and in a table it reads as maximum
  certainty.

r028 established that every promoted or declined candidate needs a sentence
of actual reasoning. A reviewer working from the slot table has a term, a
book name, and a number that is always 1.00. There is nothing there to reason
from; the reasoning material is in the staging JSON that the merge discards.

This is not a defect in the merge script — it predates this work and its row
format is the ontology's. It is a statement about what model-assisted
extraction needs that the current row cannot carry, and it should be settled
before a corpus-scale run fills sixteen slot tables with rows that cannot be
reviewed.

## r031 — the real cause of the "transient" failures

An earlier section here called provider failures transient and added retries
on that basis. That diagnosis was wrong, and the correction matters more than
the retry did.

### The model does not fit in the GPU

| | |
|---|---|
| `glm-4.7-flash` on disk | 19.02 GB |
| resident in VRAM | 6.26 GB |
| GPU total | 8.15 GB |

Two thirds of the model runs on the CPU. That is why the identical request
took 148 seconds once and exceeded 500 the next time: throughput depends on
how the layers happen to be split, which shifts with whatever else is
resident.

Of the six models installed, only two fit:

| model | size | fits in 8 GB |
|---|---|---|
| qwen2.5-coder:3b | 1.93 GB | yes |
| qwen2.5-coder:7b | 4.68 GB | yes |
| gemma4:26b / 26b-64k | 17.99 GB | no |
| qwen3-coder:30b | 18.56 GB | no |
| glm-4.7-flash | 19.02 GB | no |

Every measurement of speed taken so far was taken on a model running mostly
on the CPU, and the ~48h corpus estimate inherits that.

**This also gives the "two stable regimes" finding a simpler mechanism.**
Cold and warm runs plausibly differ because the GPU/CPU layer split differs
between loads. The observation stands — warm runs were byte-identical to each
other, cold runs to each other — but the explanation is layer placement, not
anything intrinsic to the model.

### A client timeout does not cancel the work

Observed directly: after a request timed out at 500 seconds, `/api/ps` still
showed the model loaded and busy, and a request for a *different* model
queued behind it instead of loading.

So retrying a timeout does not recover anything. It adds a second request on
top of one the server is still working through, and deepens the backlog. The
retry logic added in the previous commit would have made a slow run worse.

Timeouts are now attempted once and recorded as `provider_timeout`. Retries
are kept for genuinely unreachable endpoints, which is what they were for.

### What this changes

The choice of model for a corpus-scale run is now a measured decision rather
than a default. A 7B model resident entirely in VRAM may be several times
faster than a 30B model spilling to CPU, and the trade against extraction
quality has to be seen in data — which is the measurement currently queued
behind a stuck request.
