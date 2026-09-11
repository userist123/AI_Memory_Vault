---
id: 3f2a91c4-7d18-4e5b-9a06-52c8b41d7e39
type: procedure
lifecycle: REVIEW
category: ingestion.books
tags: ['book-ingestion', 'extraction', 'method', 'measured']
created: "2026-09-10"
updated: "2026-09-10"
provenance:
  source_type: 'execution'
  source_ref: 'session 2026-09-07..10: r030-r031, measured on 3 books and 20 PDFs'
confidence: high
verification: unverified
relations:
  - type: part_of
    target_id: slot-06-procedures
---

# Ingesting a book into the ontology

The recipe below is what survived being measured. Most of what was tried did
not, and the parts that failed are listed too — a procedure that only records
its successes sends the next person down the same dead ends.

Every number here was measured in this repository against
`06_INBOX/Carti`. The full evidence is in
[`07_EVALUATION/book_corpus_conversion/FINDINGS.md`](../../07_EVALUATION/book_corpus_conversion/FINDINGS.md).

## The recipe

```bash
python 30_SCRIPTS/ingestion/convert_pdf_to_text.py <folder> --pages-per-chunk 3
python 30_SCRIPTS/ingestion/model_extract_concepts.py \
    --input-file <book>.txt --source-book <name> \
    --output-file staging/<name>.json --rejects-file staging/<name>_rej.json \
    --provider local --model llama3.1:8b --timeout 600
```

Then review candidates with `occurrences >= 3` first. Everything else is
ordered behind them.

Four decisions in that command are load-bearing, and each was wrong at least
once before it was right.

## 1. Chunks of roughly 5,000-15,000 characters

Chunk size was confounding every selectivity measurement taken before it was
fixed.

At 10 pages a monograph chunk is ~44,000 characters holding dozens of
definable concepts. Each model returns a different subset of an over-full
passage, so disagreement between models records **what a model sampled**, not
what is weak. `Long-term potentiation` was found by 1 model of 4 at ten pages
per chunk and by 4 of 4 at three. The concept did not improve.

`--pages-per-chunk 3` is how you reach that band, but **only for books the
converter puts in `pages` mode** — 8 of the 18 usable books. A `toc` or
`font` book chunks on its own headings and the flag does nothing to it. Most
land in the band anyway:

| mode | books | chunks | median chunk |
|---|---:|---:|---|
| `toc` | 5 | 116 | 4,635 - 4,852 |
| `font` | 3 | 37 | 590 - 4,748 |
| `pages` at 3 pages | 8 | 458 | 3,595 - 12,777 |
| `pages`, forced (§4) | 2 | 313 | 6,908 - 7,194 |
| `ocr` | 2 | 164 | 8,100 - 11,915 |

**These are measured by chunking every file, and an earlier version of this
table was not.** It took chunk counts from the `headings` field in the
conversion report and sizes as `characters / headings`, which is not what
`split_into_structural_chunks()` produces — it applies its own patterns and
does not split at every detected heading. For Soar that made 22 chunks look
like 114 and a median of 47,991 characters look like 7,798.

So check the chunk size by chunking, not by dividing. Most `toc` and `font`
books sit below 5,000, and recurrence still behaved identically on a `toc`
book at a median of 4,671 — so the band's lower edge is softer than it
looks. What matters is the upper edge: a chunk over the derived limit is
skipped entirely.

**This was a stated validation limit and has since been closed for `toc`.**
The recurrence rule was first measured only on `pages`-mode books, where the
chunk boundaries are imposed by the flag. The worry was that a `toc` book,
chunked on the author's own thematic sections, would not repeat concepts
across sections at all — each idea treated once and left behind — which would
have made recurrence an artefact of page chunking rather than a property of
books.

Measured on a `toc` book (*Memory in the Age of AI Agents*, 55 chunks, median
4,671 characters): 144 candidates, 36 at `>= 2`, **13 at `>= 3`**. Squire, a
`pages` book, gave 155 / 35 / **13**. The distributions are the same, and the
`toc` book was *below* the size band while matching it.

`font`-mode books: a first attempt on Soar returned zero recurrent concepts
and meant nothing — 13 of its 22 chunks were over the limit, so 88% of the
book never reached the model. Retested after forcing it to `pages` (§4).

## 2. An instruct model, not a coder model

Not a general claim about the models — specific to being asked what counts as
a concept.

`qwen2.5-coder:7b` returns **entities**: `DNA`, `protein`, `nerve cells`,
`PET scans`, `Aplysia`, and `memory` — a term that recurs in every chunk of a
book about memory and carries no information. The instruct models return
**concepts**: `classical conditioning`, `habituation`, `synaptic plasticity`,
`nondeclarative memory`.

Recurrence precision against a four-model reference, three books:

| model | Schacter | Squire | Ashby |
|---|---:|---:|---:|
| llama3.1:8b | 92% | 82% | 70% |
| qwen2.5:7b-instruct | 67% | 100% | 100% |
| mistral:7b-instruct | 83% | 100% | 100% |
| qwen2.5-coder:7b | 58% | 40% | 80% |

## 3. Rank by recurrence, and by nothing else

Every per-candidate field the model fills in was measured and carries no
information. Do not build on any of them:

| field | what it does | why |
|---|---|---|
| `confidence_in_literature` | nothing | 1.00 on every candidate including ones whose evidence was fabricated. On a 159-candidate book it spread to five values, still clustered at the top |
| `claim_type` | nothing, and harm | constant on every survivor, and asking for it made the model swap it with `slot` — 11 of 50 rejections in one run. Removed from the request |
| prompt exclusions | nothing | the terms named as bad come straight back |

`occurrences` — how many distinct sections define the term — is the
exception, because it is counted rather than claimed. On a whole book:

    121 concepts seen once, 21 twice, 8 three times, 5 five, 2 six, 1 seven,
    1 ten

A floor of 3 leaves 17 concepts per book and roughly **210 across the whole
corpus**, which is a list a person actually reads. A floor of 2 leaves 38 per
book, ~470 corpus-wide.

Recall at that floor is 40-55% of what four models would agree on. This buys
a trustworthy core cheaply; it does not get everything.

## 4. Force `pages` mode when a book's structure detection is wrong

`--force-mode pages` on `convert_pdf_to_text.py`.

Newell's *Unified Theories of Cognition* produced 912 chunks of ~870
characters — **51% of the entire corpus run** — because font detection marked
its body paragraphs as headings at 1.63 per page. That is inside the
automatic plausibility band, so nothing catches it. Forced to pages it is 187
chunks of 6,908 characters.

Soar needs the same treatment for the opposite reason: font detection gave it
22 chunks at a median of **47,991 characters**, 13 of them over the limit, so
88% of the book was skipped in silence. Forced to pages it is 126 chunks of
7,194, none skipped.

Between them these two books are the difference between 1,581 corpus chunks
and 1,088. Check the reported `mode` and the MEASURED chunk sizes per book
before a long run — `headings_per_page` alone did not catch either of them.

## What it costs

**37 to 65 seconds per chunk** with `llama3.1:8b` on this hardware (RTX 5060
Laptop, 8 GB, `--num-ctx 16384`, model at 84% GPU residency). The corpus is
**1,088 chunks** across all 20 books, so **11 to 20 hours for one model** —
an overnight run.

The spread is wide because generation time follows output length, not input
length: Squire runs 37.2 s/chunk at a median 8,551 characters, the AI-agent
survey 64.7 s/chunk at 4,671. Smaller chunks, nearly twice the time.

Two ways that number has been got wrong here, both worth not repeating:

- **Timing the first few chunks of a book.** They are front matter, produce
  almost no output, and generation time follows output length. That produced
  a 3.0-hour estimate, wrong by 4.6x.
- **Extrapolating from one book.** Squire runs at 37.2 s/chunk and *Memory in
  the Age of AI Agents* at 64.7, despite the second having *smaller* chunks.
  Generation time is driven by how much the model has to say about a passage,
  not by how much it reads, so it is a range and not a constant.
- **Dividing instead of chunking.** The 64.7 figure was first reported as
  44.5, because the chunk count came from a metrics field rather than from
  chunking the file. Same division, same error, one layer down.

`--num-ctx` is worth checking before a long run: the window is allocated in
VRAM beside the weights, so 32768 dropped residency to 66% and cost a third
more time for the same output. 8192 is faster still and silently skips a
tenth of the chunks, because the chunk limit derives from the window.

Running four models for cross-model agreement costs four times as much and
buys a reference set, not a better result: one instruct model's recurrence
already recovers it at 70-92% precision.

## What does not work, so it is not tried again

- **Cross-model agreement as a general filter.** It works on conference
  papers, where the 1-of-4 tail is experimental furniture (`SGD optimizer`,
  `reliability plots`) and discarding it is pure gain. On monographs the same
  tail contains `long-term potentiation` and `memory consolidation`. The
  agreement distribution is stable across books — 4-of-4 at 6-9%, 1-of-4 at
  66-70% — which makes it a property of the method, not a quality signal.
- **Naming good concepts in the prompt.** A version listing four examples of
  good concepts cut apparent noise by 85%, and four of its five survivors
  were those four examples, returned regardless of the passage. That is
  extraction becoming recall, which is the r027 defect arriving through the
  prompt instead of the code.
- **Ollama's `format: "json"`.** Returns an empty string from
  `glm-4.7-flash` in under a second, every time, while the identical request
  without it answers normally.
- **A model larger than the GPU.** 19 GB against 8 GB spilled to CPU, swung
  between 148s and >500s for identical requests, and eventually stopped
  loading with `llama-server process has terminated`.

## Two failure shapes to watch for

Both cost real time in this lineage, and both look like success:

**A plausible number in front of no work.** Four instances: `_normalize()`
merging headings into paragraphs so eight books produced zero chunks while
the converter reported OK; an empty provider response counted as "this
passage defines nothing"; `deduplicate()` shipping with passing unit tests
and no caller; a fixed 24,000-character chunk limit skipping 26 of one book's
29 chunks.

**A measurement taken on the convenient sample.** Everything about book
extraction was validated on a 17-page conference paper until late, and the
first monograph exposed two defects immediately. Then the cost estimate was
taken from a book's first six chunks. Treat any paper-scale or
first-chunk number as unvalidated until re-measured where it will be used.
