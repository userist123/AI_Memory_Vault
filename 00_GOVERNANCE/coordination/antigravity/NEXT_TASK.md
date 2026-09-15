# NEXT_TASK — Antigravity

**Updated:** 2026-09-12 01:32 by Claude Code · this file is rewritten after
each of your submissions is verified. Re-read it when you finish a book.

---

## Standing rules

You read the passages yourself. No model is called, and **no program decides
what a concept is or writes a word of a definition** — not a local model, not
an API, not a regex, not a formula. Scripts are welcome for splitting,
counting, formatting and running the gate.

Full brief: `00_GOVERNANCE/coordination/antigravity/BOOK_EXTRACTION_WORK_ORDER.md`

Per book:

1. Read `scratch/agent_corpus/<name>_chunks.json`, skipping every chunk marked
   `low_prose: true`.
2. Write `scratch/agent_corpus/<name>_candidates.json`.
3. Run:
   ```bash
   python 30_SCRIPTS/ingestion/run_agent_corpus.py collect \
       --work-dir scratch/agent_corpus --agent-label antigravity
   ```
4. Run the verifier on your own work before reporting:
   ```bash
   python 30_SCRIPTS/ingestion/verify_agent_submission.py --book <name>
   ```
   If it says FAIL, fix it before moving on. It checks the things a report
   cannot: evidence present verbatim in the book, definitions not copied out of
   it, opening concentration, `low_prose` left unread, and `occurrences` equal
   to the distinct sections each concept was drawn from.
5. Claim the next book in `CURRENT.md` with an ISO timestamp and continue. Do
   not wait for confirmation between books.

**`occurrences` is the only ranking signal that carries information.** Submit
the same concept from every section that genuinely defines or explains it, each
with its own evidence quote from that section. Never twice from one section. No
ceiling and no target — if a concept is defined in forty sections it is 40, if
in two it is 2. A mention is not a definition.

Do not merge into `01_ARCHITECTURE/ontology/slots/`. Stop after the gate.

---

## Verified so far — 16 of 17 pass

| book | evidence | occurrences | coverage |
|---|---|---|---|
| newell_unified_theories | 19/19 exact | 1–3 | 12% |
| laird_soar | 21/22 exact | **flat at 1** | 18% |
| minsky_society_of_mind | 9/10 exact | 1–3 | 25% |
| schacter_tulving | 13/13 exact | 3–20 | 58% |
| ashby_design_for_a_brain | 11/11 exact | 5–47 | 91% |
| squire_kandel | 12/12 exact | 4–22 | 57% |
| why_we_forget | 13/13 exact | 3–15 | 61% |
| ashby_intro_to_cybernetics | 14/14 exact | 5–32 | 92% |
| memory_in_the_age_of_ai_agents | 14/14 exact | 3-15 | **100%** |
| 2601.09113v1 | 16/16 exact | 3-13 | 94% |
| machine_learning_report | 13/13 exact | 3-7 | 79% |
| wcs_1488 | 13/13 exact | 3-10 | 86% |
| sarfraz22a | 13/13 exact | 3-8 | 91% |
| 2504.05840v1 | 12/12 exact | 3-7 | **100%** |
| quantum_consciousness_framework | 14/14 exact | 3-5 | **100%** |
| kandel_2001 | 9/10 exact | 1–1 (4 sections, correct) | 75% |
| newell_how_can_human_mind | — | — | book is OFF SUBJECT, correctly skipped |

Zero fabricated citations, zero copied definitions, zero `low_prose` sections
read, across all of them. That is the part that is working.

---

## Queue — work down this list without stopping

```
17. wiener_cybernetics                   4 chunks,  0 low_prose
19. 7688_jkt_au                          1 chunk
20. comparison_cognitive_architectures   1 chunk
```

Books 16, 17, 19 and 20 have too few sections to reach the review floor. Still
extract them — just do not read their flat `occurrences` as a result.

---

## Then: two books to redo

**`laird_soar_cognitive_architecture`** — the only failing book. 22 concepts,
every one at `occurrences: 1`, on a book with 114 prose sections. `operator`
appears in 88 of them, `working memory` in 64, `substate` in 57, `chunking` in
43, `impasse` in 37. The concepts are right; the recurrence was suppressed.

Do not re-read the book from scratch. Use the worksheet:

```bash
python 30_SCRIPTS/ingestion/mention_worksheet.py \
    --chunks scratch/agent_corpus/laird_soar_cognitive_architecture_chunks.json \
    --candidates scratch/agent_corpus/laird_soar_cognitive_architecture_candidates.json \
    --output scratch/agent_corpus/laird_soar_cognitive_architecture_worksheet.json
```

It lists every section mentioning each of your 22 concepts, with the text
around it. For each, answer one question — **does this passage define the term,
or merely mention it?** — and add a candidate from the ones that define it.

**`minsky_society_of_mind`** — passes, but `occurrences` is pinned at 3 for
nine of ten concepts while `agent` appears in 78 sections and
`cross-exclusion` in 5. The worksheet already exists at
`minsky_society_of_mind_worksheet.json`. Same treatment.

---

## Open question, for when the queue is done

Two concepts are proposed for two different slots, and the merge now stops
rather than writing both:

```
semantic memory   retrieval (soar)        ontology (schacter)
explicit memory   ontology (kandel_2001)  retrieval (schacter)
```

Neither is a mistake — Soar treats semantic memory as a store you retrieve
from, Schacter as a distinct system. Say which reading the vault should hold and
why; do not merge anything either way.
