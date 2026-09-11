# Book corpus conversion — measured findings

Packages: `r030/pdf-text-frontend`, `r031/model-assisted-extraction`
Measured: 2026-09-07 to 2026-09-10, against `06_INBOX/Carti` (20 PDFs, 151 MB)

## Read this first

This document is a running log, appended section by section as things were
measured. **Several early sections were later shown to be wrong, and the
retractions are further down rather than edited in place** — that is
deliberate, because how a wrong number was produced is usually more useful
than the right one. Do not quote a figure from the middle of this file
without checking whether a later section withdrew it.

The state as of the last section:

| | |
|---|---|
| conversion | works; **20 of 20 books**, the 2 scans OCR'd once Tesseract was installed |
| OCR quality | Minsky clean; Ashby has 13% of pages in scrambled column order — unblocked, not recovered |
| extraction at book scale | works, with gates that catch fabricated evidence |
| selectivity | `occurrences >= 3`, validated on all three structure modes; 160-290 candidates corpus-wide |
| recall | 40-55% of what four models agree on |
| cost | **11.2-19.6h** for one model over 1,088 measured chunks: 37.2 s/chunk on one book, 64.7 on another |

The method, and everything that failed on the way to it, is written up as a
procedure: [`10_DOCUMENTATION/procedures/Ingesting_A_Book_Into_The_Ontology.md`](../../10_DOCUMENTATION/procedures/Ingesting_A_Book_Into_The_Ontology.md).
Read that to *use* the pipeline. Read this to see what the numbers were and
which of them did not survive.

**Claims withdrawn later in this file:** that extraction at book scale does
not work (it does, once chunk size is right); that `occurrences` is a dead
signal (wrong resolution, not dead); that cross-model agreement is a general
selectivity filter (papers only); that a single model's recurrence is 100%
precise (a normalizer bug — really 70-92%); and that a corpus run takes 3
hours (13.6).

---

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

## r031 — a second model, and the gate it walked through

`qwen2.5-coder:7b` (4.68 GB, the largest installed model that fits entirely
in the 8 GB GPU) was run over three chunks of the same paper.

Its definitions are good — arguably better than the 30B model's:

> **Episodic Memory** — A type of memory that stores specific instances of
> events or experiences.

But every one of its eight candidates carried:

    "claim_type": "ontology"      <- ontology is a SLOT, not a claim type
    "slot":       "identity"      <- all eight, regardless of subject

The two fields were swapped, and **the pipeline accepted all eight**, because
`validate()` checked the slot and never checked `claim_type` at all. Eight
rows would have entered the `identity` slot table carrying a claim type that
does not exist in the schema.

An unvalidated field is a field the model may fill with anything. `claim_type`
is now checked against the five documented values, with a distinct rejection
reason when the value is a slot name — because that specific confusion means
the model answered the wrong question and the slot field cannot be trusted
either.

This was only visible because a second model was tried. The 30B model happens
to fill the field correctly, so the missing gate was invisible for as long as
one model was used.

### Model comparison, such as it is

| | glm-4.7-flash (19 GB, spills to CPU) | qwen2.5-coder:7b (4.7 GB, fits) |
|---|---|---|
| candidates kept (3 chunks) | 13 | 8 |
| verbatim rejections | 1 | 8 |
| claim_type correct | yes | no, all wrong |
| slot spread | 4 slots | 1 slot, all `identity` |
| definition quality | good | good |

The 7B model copies source phrasing far more (8 verbatim rejections against
1) and cannot keep the two schema fields apart. The larger model is better at
the structured part of the task even while running mostly on CPU.

Timing is not comparable between the two runs: the 7B run spent an unknown
part of its 14 minutes queued behind the stuck 30B request described above.
Speed still needs a clean measurement on an idle endpoint.

## r031 — clean speed, and the review problem returns

Measured on an idle endpoint, same chunk, same seed, each model unloaded
before the next:

| model | time | throughput | outcome |
|---|---|---|---|
| qwen2.5-coder:7b | **21.7s** | 43.8 tok/s | 8 objects parsed |
| glm-4.7-flash | — | — | **crashed**: `llama-server process has terminated` |

The 30B model is not merely slow on this machine. It no longer loads: a 19 GB
model against an 8 GB GPU now fails outright rather than spilling. The earlier
148s and 500s figures were taken while it still managed to load.

So the model choice is settled by the hardware, not by preference. At 21.7s
per chunk the corpus is roughly **7 hours**, against the ~48h estimated from
the CPU-bound measurements.

### What the gates catch on this model

Six chunks, full validation:

| | |
|---|---|
| proposed by the model | 60 |
| **kept** | **10** (17%) |
| verbatim_ngram | 17 |
| slot_unknown | 14 |
| claim_type_is_a_slot | 11 |
| definition_short | 5 |
| term_shape / paraphrase_shallow | 3 |

The field confusion runs in both directions: `ontology` appears in
`claim_type`, and `definition` appears in `slot` — 14 times, which is the
entirety of `slot_unknown`. The claim_type gate added in the previous commit
catches 11 candidates that would otherwise have entered the ontology.

`claim_type` carries no information from this model even when it validates:
all 10 survivors say `definition`, none says mechanism, finding, taxonomy or
constraint. Like confidence, it should not be read as a signal.

### The review load, now real

10 candidates from 6 chunks is 1.67 per chunk. Across 1,107 chunks that is on
the order of **1,850 candidates**.

The original plan set a stop condition at ~300 for the whole corpus, on the
reasoning that a queue nobody can review is backlog rather than memory. The
rule-based extractor came in far under it at 112 — which turned out to be
because its output was mostly unusable. Model-assisted extraction produces
candidates worth reviewing and produces roughly sixteen times as many.

The volume question was answered "not a problem" earlier in this document
against the rule-based path. **Against this path it is the binding
constraint**, and it is a decision rather than a defect: ingest fewer books,
raise the bar for what counts as load-bearing, or accept a review queue in
the thousands.

Nothing here should proceed to a corpus run until that is settled.

## r031 — an attempted fix that failed, and why it looked like it worked

The full paper, 12 chunks, three prompt versions, same model and seed:

| prompt | candidates | setup noise present |
|---|---|---|
| v1 original | 35 | yes — half the list |
| v2 + exclusions + 4 good examples | **5** | none |
| v3 + exclusions only | **48** | yes, all of it back |

v2 looks like an 85% reduction with the noise eliminated. It is not a fix.

Four of v2's five survivors were **the four concepts the prompt named as
good examples**: catastrophic forgetting, episodic memory, synaptic
consolidation, stability-plasticity trade-off. The occurrence histogram shows
one of them returned in seven of twelve chunks and three others in four each
— regardless of what the passage said. The model was not filtering better, it
was repeating the prompt.

That is r027's defect — extraction that is really recall — reintroduced
through the prompt instead of the code. The existing regression test for it
uses `FakeModelProvider` and therefore never sees the prompt at all, so
nothing in the suite could have caught it. `test_the_prompt_names_no_desirable_concept`
now asserts against the prompt text directly.

v3 removes the positive examples and keeps the exclusions. The result settles
the question: **the exclusion list does not work.** Every term it names by
example — `buffer size`, `SGD optimizer`, `grid search`, `random crop`,
`Rot-MNIST`, `backbone`, `hyperparameters`, `number of training epochs` —
comes back, alongside `GCIL-U`, `GCIL-L` and `S-TinyImageNet`. Yield is
higher than the original 35.

### Where that leaves the selectivity problem

- **Model confidence**: constant. Cannot rank.
- **claim_type**: constant, and swapped with slot on this model. Cannot rank.
- **occurrences**: 47 of 48 concepts appear exactly once. Cannot rank.
- **Prompt-level exclusion**: ignored by the model. Does not filter.

Every mechanism tried for separating load-bearing concepts from experimental
furniture has now been measured and none of them works. The candidates are
individually reasonable and roughly half of them are things like "validation
set" and "ReLU units".

Prompt engineering is not going to fix this on this model, and the honest
version of the pipeline is the one that yields ~48 per paper with half of it
noise — not the one that yields 5 by echoing its own instructions.

A corpus run at this rate is ~4,400 candidates. That is not a queue anyone
reviews.

## r031 — cross-model agreement, the first selectivity signal that works

Four 7-8B models installed and run over the same six chunks of the same
paper, identical seed and temperature, each unloaded between runs:

| model | kept | verbatim rejections | GPU residency |
|---|---:|---:|---|
| qwen2.5-coder:7b | 22 | 14 | 92% |
| llama3.1:8b | 16 | 10 | 66% |
| mistral:7b-instruct | 10 | 11 | 69% |
| qwen2.5:7b-instruct | 5 | 8 | 92% |

The spread is the point. If all four returned roughly the same concepts there
would be nothing to separate, and agreement would be as constant as
confidence. One model returning 5 and another 22 from the same text means
there is a core everyone sees and a periphery only one does.

### What agreement separates

30 distinct concepts across the four runs:

| found by | n | share | examples |
|---|---:|---:|---|
| 4 of 4 | 3 | 10% | Reservoir sampling, Semantic memory, Synaptic consolidation |
| 3 of 4 | 5 | 17% | Continual learning, Episodic memory, Fisher information matrix, stability-plasticity trade-off |
| 2 of 4 | 4 | 13% | CLS-ER, Dual memory system, Exponential moving average |
| **1 of 4** | **18** | **60%** | Overall loss, Supervised loss, Soft-targets, reliability plots, Representation space, Decision boundaries |

The tail is the experimental furniture that no other mechanism could remove —
not the prompt exclusions, not occurrences, not confidence.

Unlike every signal tried before it, this one is **counted rather than
claimed**, and no single model can inflate it because none of them sees the
others' answers.

### What it costs, and what it loses

**Compute scales with models.** Four models over 1,107 chunks is roughly 27
hours of local inference against about 7 for one.

**It discards true positives.** `Catastrophic forgetting` and `Experience
replay` were each found by exactly one of four models. Both are load-bearing.
A threshold of 2 loses them. Agreement measures how *obvious* a concept is to
several readers, which is close to but not the same as how important it is.

So `--min-models` defaults to 1: every concept is kept and merely annotated
with `models_agreeing` and `found_by`. A filter that silently drops real
concepts should be something a person turns on deliberately, not a default.

### Two ways this could stop being evidence, both guarded

- **Counting a model against itself.** Two runs of one model are one opinion.
  The tool identifies the model from the rows rather than the filename and
  refuses duplicates outright.
- **Folding terms too eagerly.** Agreement is worthless if `Supervised loss`
  and `Overall loss` merge. The first version was also wrong in the other
  direction — a bare trailing-s strip turned `approaches` into `approache`,
  which failed to match `approach` on the first real pair it met. Both
  directions are now parametrized tests.

### Note on residency

`llama3.1:8b` reports 66% on GPU and `mistral:7b-instruct` 69%, despite both
being under 5 GB on disk. The 32k context window is what pushes them over an
8 GB card; the two qwen 7b models fit at 92%. Lowering `--num-ctx` is the
lever if their speed matters.

## r031 — agreement does not transfer to monographs

The previous section reported cross-model agreement as the first selectivity
signal that works, measured on six chunks of a 17-page conference paper. Run
on a real monograph it behaves differently, and the difference matters more
than the similarity.

Eight chunks of Schacter & Tulving, *Memory Systems 1994*, four models, same
settings:

| found by | n | share | what is in it |
|---|---:|---:|---|
| 4 of 4 | 1 | 2% | memory system |
| 3 of 4 | 8 | 12% | declarative / episodic / semantic / procedural memory, locale system, taxon system, multiple memory systems, relational representations |
| 2 of 4 | 11 | 17% | hippocampus, pattern separation, conjunctive encoding, configural learning, recency |
| **1 of 4** | **45** | **69%** | **long-term potentiation, memory consolidation, cognitive learning, taxon learning, maplike representations** |

The top is excellent — that 3-of-4 row is the core vocabulary of the book.

**The tail is not noise.** On the paper, the 1-of-4 group was `Overall loss`,
`SGD optimizer`, `reliability plots` — experimental furniture, and discarding
it was pure gain. Here the same group contains `long-term potentiation` and
`memory consolidation`, which are load-bearing for this vault by any reading.
A `--min-models 2` filter would throw both away.

### Why, and it is not a property of the books

A monograph chunk in `pages` mode is ~44,000 characters and contains dozens
of definable concepts. A paper chunk is ~4,600 and contains a few. Asked for
a list, each model returns a different subset of an over-full passage — so
disagreement records **which concepts a model happened to sample**, not which
concepts are weak.

The 4-of-4 share supports that reading directly: 10% on the paper against 2%
here. Convergence falls as the passage gets fuller, which is what sampling
predicts and what quality would not.

### What this changes

Agreement filters noise where noise exists as a distinct population. It is
not a general quality signal, and the earlier section should be read with
this one. On monographs it currently measures chunk over-fill.

The testable consequence: if this is sampling, then **smaller chunks should
raise convergence**, because a passage with three concepts in it leaves the
models less room to differ. `convert_pdf_to_text.py --pages-per-chunk` is the
lever — it defaults to 10. That is the next measurement, and if convergence
does not rise with smaller chunks then the sampling explanation is wrong and
something else is going on.

### Also measured here

The grounding fix from the previous commit is visible in this run:
llama3.1:8b went from 9 kept to 18, with `evidence_not_in_source` falling
from 32 to 21. mistral went from 4 such rejections to 1.

## r031 — chunk size was the confound, and recurrence comes back

The previous section predicted that if monograph disagreement was chunk
over-fill, smaller chunks would raise convergence. Same book, same text span,
same four models; only `--pages-per-chunk` changed, 10 to 3. That turned 29
chunks at a median of 44,614 characters into 95 at 12,777.

The prediction had two halves and the result splits them:

| | 8 big chunks | 24 small chunks |
|---|---:|---:|
| distinct concepts | 65 | 138 |
| found by 4 of 4 | 2% | **6%** |
| found by 1 of 4 | 69% | **69%** |

**Confirmed:** convergence at the top tripled.
**Refuted:** the tail did not shrink at all. It stayed at exactly 69%.

Both are explained by the same thing: smaller chunks surface far more
concepts — 138 against 65 over the same pages — and the newly visible ones
are themselves mostly singletons. The gain is resolution, not selectivity.

The cleanest single piece of evidence for the sampling account:
**`Long-term potentiation` was found by 1 of 4 models at 10 pages per chunk
and by 4 of 4 at 3 pages.** The concept did not improve. It stopped competing
for attention with dozens of others in the same passage.

### `occurrences` was not a dead signal, it was measured at the wrong resolution

Earlier in this document `occurrences` is recorded as useless: 1 for 47 of 48
concepts. That measurement was taken on large chunks, where a concept appears
once because one chunk covers the whole chapter it belongs to.

At 3 pages per chunk a book returns to its central ideas across chunk
boundaries, and recurrence becomes visible again. What `llama3.1:8b` alone
found in more than one section:

    Configural learning, declarative memory, declarative memory system,
    delayed conditional discrimination, episodic memory, long-term
    potentiation, memory system, multiple memory systems, pattern separation,
    procedural memory, relational representations, semantic memory,
    spatial memory

That is close to the core vocabulary of the book, from one model.

### Recurrence approximates agreement at a quarter of the cost

Taking the 3-of-4 agreement set (20 concepts, four runs) as the reference,
and asking what one model's `occurrences >= 2` recovers from a single run:

| model | recurrent concepts | how many are in the agreement core |
|---|---:|---|
| **llama3.1:8b** | 11 | **11 (100%)** |
| mistral:7b-instruct | 5 | 4 (80%) |
| qwen2.5:7b-instruct | 2 | 2 (100%) |
| qwen2.5-coder:7b | 12 | 7 (58%) |

`llama3.1:8b` at 3-page chunks with a recurrence floor of 2 produced eleven
concepts and **every one of them was in the four-model core**. Precision was
perfect on this sample; recall was 55% of the core, so it is a high-precision
subset rather than a replacement.

Model choice is not interchangeable here: the same rule on `qwen2.5-coder:7b`
is only 58% precise.

### Honest limits

- One book, 24 chunks, one span. This is a promising result, not an
  established one, and it should be repeated on a second monograph before
  anything is built on it.
- Recall is 55%. Half the core is missed.
- Smaller chunks double the total candidate count, so the review-load problem
  gets *worse*, not better — the filter is what makes it tractable, and the
  filter's recall is the open question.
- Cost per corpus run at 3 pages per chunk has not been measured; the chunk
  count roughly triples while each call gets cheaper.

## r031 — replicated on a second monograph

The previous section's result was explicitly marked as one book and not
established. Repeated on Squire & Kandel, *Memory from Mind to Molecules* —
a different subject (molecular neurobiology rather than memory-system
taxonomy), same protocol: 3 pages per chunk, 24 chunks, four models.

### The agreement distribution is a property of the method, not of one book

| | Schacter & Tulving | Squire & Kandel |
|---|---:|---:|
| distinct concepts | 138 | 115 |
| found by 4 of 4 | 6% | 8% |
| found by 1 of 4 | 69% | 70% |

Within a point or two on both ends. The ~70% singleton tail is what this
method does at this chunk size, on any book.

### Recurrence replicates, and separates the models sharply

Against the 3-of-4 agreement set as reference (20 concepts on each book):

| model | Schacter precision | Squire precision |
|---|---:|---:|
| **llama3.1:8b** | 11/11 (100%) | **8/8 (100%)** |
| qwen2.5:7b-instruct | 2/2 (100%) | 5/5 (100%) |
| mistral:7b-instruct | 4/5 (80%) | 2/2 (100%) |
| qwen2.5-coder:7b | 7/12 (58%) | **7/19 (37%)** |

Recall for llama3.1:8b is 8 of 20 here against 11 of 20 before — 40-55%
across the two books. High precision, partial recall.

### The coder model is not noisy, it is answering a different question

Its recurrent terms on Squire:

    Aplysia, DNA, PET scans, behaviorism, brain structures, messenger RNA,
    memory, nerve cells, protein, synaptic vesicle, ...

against llama3.1:8b's:

    classical conditioning, habituation, long-term memory, nondeclarative
    memory, short-term memory, synaptic plasticity, synaptic potential,
    synaptic strength

The coder model extracts **entities** — nouns that recur in any biology text.
The instruct models extract **concepts**. That is a systematic difference in
what each treats as definable, not random error, and it is why its recurrence
is 37% precise while its raw yield is the highest of the four.

`memory` appears in its list, which is the clearest single illustration: a
term that recurs in every chunk of a book about memory and carries no
information.

### Where this leaves the pipeline

Established on two books, with the limits stated:

- **Chunk at 3 pages, not 10.** Chunk size was confounding every earlier
  selectivity measurement.
- **Use an instruct model, not a coder model**, for extraction. This is not a
  general claim about the models — it is specific to being asked what counts
  as a concept.
- **`occurrences >= 2` from one instruct model** recovers a high-precision
  subset of what four models agree on, at a quarter of the compute.
- Recall is 40-55%. This is a way to get a trustworthy core cheaply, not a
  way to get everything.

Still unmeasured: cost of a full corpus run at 3 pages per chunk, and whether
precision holds on the books that are neither memory-systems taxonomy nor
molecular neurobiology — Ashby, Newell, and the cognitive-architecture group.

## r031 — correction: the 100% precision was a bug of ours

Ashby's *Design for a Brain* was run as a third book, deliberately outside
the family the first two shared — 1950s cybernetics rather than memory
research. Its output contained `Dynamic Systems` and `dynamic system` as two
separate concepts in one model's run, which is what exposed the defect.

**Two normalizers disagreed about what makes two terms the same concept.**
Within-run deduplication in `model_extract_concepts.py` lowercased and
collapsed whitespace. `agree_across_models.normalize_term` also folded
plurals, hyphens and parenthetical glosses. So inside a single run a concept
named two ways stayed two rows at `occurrences: 1`, and neither reached a
recurrence floor of 2 — while the agreement tool, comparing across runs,
counted them as one.

Recurrence was undercounted everywhere it was measured. And because the
undercount kept only the terms that happened to be spelled identically every
time, it removed the near misses and **inflated the measured precision**.

### The corrected numbers

Precision of `occurrences >= 2` against each book's 3-of-4 agreement core:

| model | Schacter | Squire | Ashby |
|---|---:|---:|---:|
| llama3.1:8b | 92% (11/12) | 82% (9/11) | 70% (7/10) |
| qwen2.5:7b-instruct | 67% (2/3) | 100% (5/5) | 100% (3/3) |
| mistral:7b-instruct | 83% (5/6) | 100% (2/2) | 100% (2/2) |
| qwen2.5-coder:7b | 58% (7/12) | 40% (8/20) | 80% (4/5) |

**The "11/11 and 8/8, perfect precision" reported in the two previous
sections is withdrawn.** The real figures for llama3.1:8b are 92%, 82%, 70%,
and they fall as the book moves away from neuroscience.

### What survives the correction

- The agreement distribution is still stable across all three books: 4-of-4
  at 6%, 8%, 9%; 1-of-4 at 69%, 70%, 66%. That measurement did not depend on
  the broken normalizer.
- Instruct models still beat the coder model on recurrence precision in five
  of six book-model pairs, and the qualitative reason still holds — the coder
  model returns entities (`DNA`, `protein`, `memory`) where instruct models
  return concepts.
- Recurrence at 3 pages per chunk is still a usable high-precision signal
  from a single model. It is 70-92% rather than 100%, which is a different
  claim and should be planned against as such.

The normalizers are now one function, imported rather than reimplemented, and
`test_extraction_and_agreement_fold_terms_identically` fails if they ever
diverge again.

## r031 — what a corpus run actually costs

Measured rather than estimated. `llama3.1:8b` over six 3-page chunks of
Squire took 59.5 seconds wall clock — **9.9 seconds per chunk**, with the
model at 66% GPU residency.

Corpus chunk count, counting `toc` and `font` books by their own structure
and `pages` books at 3 pages per chunk:

| | chunks | one model | four models |
|---|---:|---:|---:|
| as detected | 1,798 | 4.9h | 19.8h |
| **with Newell forced to `pages`** | **1,073** | **3.0h** | 11.8h |

### One book was half the run

Newell's *Unified Theories of Cognition* produced **912 of the 1,798
chunks — 51% of the entire corpus** — because font-mode detection marked its
body paragraphs as headings at 1.63 per page. That sits *inside* the
plausibility band of 0.05-2.0, so the automatic guard passes it. Its median
chunk is 867 characters: too small to hold a definition and its context.

It is also the book with 14 OCR-degraded pages. Half the compute would have
gone to the worst-structured, worst-quality text in the corpus, at the
resolution least likely to yield anything.

Forced to `pages` at 3 pages per chunk it is 187 chunks with a median of
6,908 characters, and the corpus drops from 4.9 hours to 3.0.

`convert_pdf_to_text.py --force-mode pages` exists for this. The band cannot
catch every failure — a book can sit inside it and still be wrong — so the
override is deliberate and per-book rather than a widened threshold that
would change every other book too.

### The overnight question, answered

A single-model corpus run at 3 pages per chunk is **about three hours**. That
is an evening, not an overnight job, and well within what this hardware does
unattended.

The four-model agreement run is 11.8 hours. Given that recurrence from one
instruct model recovers 70-92% precision against the four-model core, the
four-model run costs four times as much for a reference set rather than a
better result.

What is still not answered by any of this: recall is 40-55%, and the review
load at the far end is unchanged — three hours of compute still produces more
candidates than anyone has agreed to read.

## r031 — one whole book, and a cost estimate that was wrong by 4.6x

Squire & Kandel run end to end with `llama3.1:8b`, all 87 chunks at 3 pages.

### The review load is bounded

| threshold | per book | corpus estimate (1,073 chunks) |
|---|---:|---:|
| all candidates | 159 | ~1,960 |
| `occurrences >= 2` | 38 | ~470 |
| **`occurrences >= 3`** | **17** | **~210** |

Occurrence histogram over the book: 121 concepts seen once, 21 twice, 8 three
times, 5 five times, 2 six, 1 seven, 1 ten.

At a floor of 3 the list is the book:

    declarative memory (10), synaptic plasticity (7), long-term memory (6),
    classical conditioning (6), short-term memory (5), nondeclarative memory
    (5), medial temporal lobe (5), hippocampus (5), amygdala (5), habituation
    (3), cerebellum (3), NMDA receptor (3), consolidation (3), ...

**~210 candidates corpus-wide is under the 300 stop condition** this project
set for itself at the outset. The volume problem, which has been the binding
constraint since the first measurement, is answered — by a recurrence floor
of 3 rather than by anything clever.

### The cost estimate in the previous section was wrong

It said 9.9 seconds per chunk and 3.0 hours for the corpus. The whole book
took **66 minutes for 87 chunks — 45.8 seconds per chunk**, and the corpus is
therefore **about 13.6 hours** for one model.

The 9.9-second figure came from timing the first six chunks. Those are front
matter — title pages, contents, sparse text — and produce almost no output.
Dense prose produces far more, and generation time follows output length.

That is the same error corrected twice already in this document: measuring
the convenient sample and extrapolating from it. Timing the *first* chunks of
a book is a systematically optimistic sample, not a random one.

So the corpus run is an overnight job after all, which was the original
intuition. It is one night, not three, and it produces roughly 210 reviewable
candidates rather than four thousand unreviewable ones.

### Also observed

Confidence finally varied — five distinct values (0.7, 0.8, 0.9, 0.95, 1.0)
across 159 candidates, against one value in every earlier run. That is a
consequence of the larger sample, not of calibration: it is still clustered
at the top and still should not be used to rank anything.

Slots spread across 13 of 16 for the first time: identity 38, ontology 36,
procedures 24, map 16, relationships 14, state 9, constraints 9,
consolidation 6, and single digits elsewhere.

## r031 — the two scanned books, recovered unevenly

Tesseract installed, so the blocker recorded since r030 is gone. Both scans
OCR'd at 200 dpi: **492 pages in 6 minutes 23 seconds**, 0.78 seconds per
page, 1,521,912 characters.

| | pages | chars | degraded pages | column artefacts |
|---|---:|---:|---:|---:|
| Minsky, *Society of Mind* | 336 | 916,188 | **0** | **0** |
| Ashby, *Introduction to Cybernetics* | 156 | 605,724 | 1 (1%) | **20 (13%)** |

Minsky is clean enough to read directly:

> "Up to this point we've portrayed the mind as made of scattered fragments
> of machinery. But we adults rarely see ourselves that way; we have more
> sense of unity."

### Ashby is unblocked, not recovered

Its character-level quality is fine — one degraded page in 156. The problem
is layout: it is set in two columns with equations, and Tesseract reads the
page image in horizontal lines, merging the columns:

> "...to find how the transform follows **There results the transducer** from
> the operand, shows that in all cases..."

Two sentences from two columns, interleaved. The degraded-page detector
cannot see this, because every individual word is correct — only the order is
wrong. 13% of pages are affected, and they are the technical ones; the
narrative pages are clean.

`page.get_text(textpage=..., sort=True)` does **not** fix it. Block sorting
runs after Tesseract has already merged the columns into single lines. A real
fix needs column detection and per-column OCR, which is substantial work.

So the honest statement is 20 of 20 books have text, and one of them has 13%
of its pages in scrambled sentence order. Concept extraction from those pages
will produce definitions built from two half-sentences, and the grounding
check will not catch it — the scrambled text *is* in the source.

The difference between the two books is the layout, not the OCR: single-
column narrative recovers perfectly, two-column technical does not.

## r031 — the context window re-measured, and a second chunking path caught

### Controlled comparison, one variable

Squire & Kandel end to end, `llama3.1:8b`, same seed, only `num_ctx` changed:

| | 32768 | 16384 |
|---|---:|---:|
| wall clock | 66m28s | **53m54s** |
| per chunk | 45.8s | **37.2s** |
| GPU residency | 66% | 84% |
| candidates kept | 159 | 155 |
| `occurrences >= 2` | 38 | 35 |
| `occurrences >= 3` | 17 | 13 |

19% faster for materially the same output.

**But the recurrence core is not stable across configurations.** 17 concepts
against 13, with only 12 shared. Both runs used temperature 0 and a fixed
seed; the context window alone moved the `>= 3` set by about five concepts.
The "~210 candidates corpus-wide" figure should be read as an order of
magnitude, not a count.

### A second chunking path, quietly using a different size

The OCR path emitted one heading per page. Measured: Minsky at a median of
2,892 characters per chunk and Ashby at 4,302 — **below the 5,000-15,000 band
that every recurrence measurement was taken in.**

Chunk size was the confound behind every earlier selectivity result in this
document. A second code path silently using a different one is that same
mistake with a new name, and it was introduced an hour after the band was
written down.

Fixed to group pages the way `pages` mode does:

| | before | after |
|---|---|---|
| Ashby | 150 chunks, 4,302 median | **53 chunks, 11,915 median** |
| Minsky | 332 chunks, 2,892 median | **111 chunks, 8,100 median** |

### Corpus cost, current

    1,073 chunks (18 books)  +  164 (the two OCR books)  =  1,237
    1,237 x 37.2s = 12.8 hours for one model

Down from 13.7h and now including all 20 books rather than 18.

## r031 — recurrence holds on a `toc` book, and per-chunk time varies by book

The procedure recorded a limit: the recurrence rule had only been measured on
`pages`-mode books, where the chunk boundaries are ones we impose. The
concern was specific — a `toc` book is chunked on the author's own thematic
sections, so a concept might be treated once and left behind, never
recurring. That would make recurrence an artefact of page chunking rather
than a property of books.

It is not. *Memory in the Age of AI Agents*, `toc` mode, 80 chunks at a
median of 5,550 characters:

| | toc book | Squire (`pages`) |
|---|---:|---:|
| candidates | 144 | 155 |
| `occurrences >= 2` | 36 | 35 |
| `occurrences >= 3` | **13** | **13** |

Histogram: 108 seen once, 23 twice, 10 three times, 2 four, 1 six.

The `>= 3` set reads as the book's actual subject matter: KV cache, LLM
memory, working memory, retrieval-augmented generation, context engineering,
latent memory, memory slots, multimodal memory, K-nearest-neighbour search.

`font`-mode books remain untested.

### Per-chunk time is not a constant

| book | mode | median chunk | per chunk |
|---|---|---:|---:|
| Squire & Kandel | pages | 8,551 chars | 37.2s |
| Memory in the Age of AI Agents | toc | 5,550 chars | **44.5s** |

Smaller chunks, slower per chunk — so generation time is driven by how much
the model has to *say* about a passage, not by how much it reads. The corpus
estimate is therefore a range rather than a number:

    1,237 chunks x 37.2s = 12.8 hours
    1,237 chunks x 44.5s = 15.3 hours

The header table carries the range. A single-book measurement extrapolated to
a corpus is the same mistake as timing a book's first six chunks, one level up.

## r031 — every corpus-scale number in this file was derived, not measured

A `font`-mode validation run finished in 2m53s instead of the expected 80
minutes and produced zero recurrent concepts. The result looked like a clean
negative. It was not a result at all: 13 of Soar's 22 chunks exceeded the
chunk limit, so the run covered 12% of the book.

Pulling that thread invalidated the chunk counts, the cost estimate, the
per-mode size table in the procedure, and the description of a validation run
already reported as successful.

### The root cause

Chunk counts for `toc` and `font` books were taken from the `headings` field
in `conversion_metrics.json`, and chunk sizes as `characters / headings`.
Neither is what `split_into_structural_chunks()` actually produces — it
applies its own patterns to the text and does not split at every detected
heading.

For Soar that made 22 chunks look like 114, and a median of 47,991 characters
look like 7,798 — wrong by a factor of six, in the direction that hides a
problem.

### Measured, by actually chunking every file

| mode | books | chunks | median chunk |
|---|---:|---:|---|
| `toc` | 5 | 116 | 4,635 - 4,852 |
| `font` | 3 | 37 | 590 - 4,748 |
| `pages` | 8 | 458 | 3,595 - 12,777 |
| `pages` (forced) | 2 | 313 | 6,908 - 7,194 |
| `ocr` | 2 | 164 | 8,100 - 11,915 |

**1,088 chunks**, against 1,237 claimed earlier and 1,581 before Soar and
Newell were forced to `pages`. Soar goes from 22 chunks with 13 skipped to
126 with none; Newell from 784 chunks of 867 characters to 187 of 6,908.

Only 4 chunks in the whole corpus now exceed the limit, one each in four
books.

### The corpus had never been converted the way the procedure documents

All the 3-page work — Schacter, Squire, Ashby, the `toc` survey — ran on
copies in a scratch directory. The files under `06_INBOX/Carti` were still
the original 10-page conversion. The recipe was written up before the corpus
it describes had ever been produced.

It has been now: `--pages-per-chunk 3 --ocr`, then Soar and Newell
re-converted with `--force-mode pages`.

### A reported run described with numbers it did not have

The `toc` validation was reported here as "80 chunks, median 5,550". The
script printed `toc-mode book: 55 chunks, median 4671 chars (band is
5000-15000)` as its first line — including the parenthetical flagging that
the book sits *below* the documented band. That line was missed by tailing
the output.

The validation's conclusion survives, because it came from the output JSON:
13 concepts at `occurrences >= 3`, the same as Squire's 13. If anything it is
a stronger result than claimed — recurrence held at a median chunk of 4,671,
below the band the rule was supposed to need.

The timing does not survive. 59m19s over 55 chunks is **64.7 s/chunk**, not
the 44.5 computed against 80. So the corpus is **11.2 to 19.6 hours**, a
wider spread than any figure given for it so far.

## r031 — `font` mode validated, and the per-book yield varies by 2.5x

Soar, forced to `pages` so it actually fits, 126 chunks:

    306 candidates, 71 at >= 2, 34 at >= 3

The `>= 3` list is the book's own vocabulary, with counts:

    substates (16), semantic memory (13), episodic memory (13), impasse (11),
    working memory (10), cognitive architecture (8), chunking (8), operator
    (8), knowledge search (7), procedural knowledge (7), problem space (6),
    PSCM (5), PEACTIDM (4)

That is the prediction made before the run, and it closes the last stated
validation gap: all three structure modes behave the same way.

### But the yield rate does not transfer between books

| book | mode | chunks | at `>= 3` | per chunk |
|---|---|---:|---:|---:|
| Squire & Kandel | pages | 87 | 13 | 0.149 |
| Memory in the Age of AI Agents | toc | 55 | 13 | 0.236 |
| The Soar Cognitive Architecture | pages (forced) | 126 | **34** | **0.270** |

Across 1,088 corpus chunks that is **160 to 290 candidates** at a floor of 3,
not the "~210" this document has carried since it was computed from one book.
210 sits inside the range by coincidence rather than by derivation.

Soar being the high end is not a defect: it is a densely technical book about
a single architecture, so its vocabulary genuinely recurs. A per-book cap
would punish exactly the book with the most to say.
