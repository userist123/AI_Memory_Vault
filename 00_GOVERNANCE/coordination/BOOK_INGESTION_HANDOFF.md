# Handoff: book ingestion — for Antigravity, Codex, ChatGPT, Perplexity

**Author:** Claude Code (r034) · **Date:** 2026-09-11 · **Status:** ready to execute

This brief hands the extraction work to an agent that can read well. It is written
to be executed without this conversation.

## Who does what

| Agent | Access | Suited to |
|---|---|---|
| Antigravity | repo + local | reading chunks, running the gate locally, long batches |
| Codex | repo + local | same, plus the script changes if a gate needs tuning |
| ChatGPT | repo | reading chunks, returning `candidates.json` for someone else to gate |
| Perplexity | repo | cross-checking a candidate against outside literature — not extraction |

**Do not route this to a local Ollama model.** The owner's instruction is explicit:
online agents only. The models that fit an 8 GB card are 7-8B and the measured
failure modes are not marginal — fabricated evidence quotes, invented ontology
slots, definitions that were the source sentence with two words changed.
`scratch/run_corpus_ingestion.py` on `r033/corpus-ingestion` still passes
`--provider local`; that path is superseded by this one.

## The task

For each converted book, read the chunks and propose candidate concepts for the
sixteen canonical ontology slots. Every candidate is then run through gates that
were calibrated on measured populations, not on one model's weaknesses.

## Inputs

The corpus is already converted and chunked. `06_INBOX/Carti/**/*.txt` sits next
to each PDF (untracked — `06_INBOX/` is the inbox, nothing is promoted out of it).

- 20 books, **1,120 chunks**, 0 over the context limit, 0% of content skipped
- two books were OCR'd: Minsky recovered clean; Ashby is unblocked, not recovered
  (~13% column interleaving — treat its candidates with suspicion)

**The whole corpus is already prepared** under `scratch/agent_corpus/`, one
`<name>_chunks.json` per book, plus a `manifest.json` giving the order. Do them
in that order — it is largest-book-first, and the reason is in
`run_agent_corpus.py`: recurrence cannot be measured on a book with fewer
sections than the review floor, so a small book's yield is not a result.

To re-prepare, or to gate everything written back so far:

```bash
python 30_SCRIPTS/ingestion/run_agent_corpus.py prepare --work-dir scratch/agent_corpus
python 30_SCRIPTS/ingestion/run_agent_corpus.py collect     --work-dir scratch/agent_corpus --agent-label antigravity
```

`collect` is resumable: run it whenever a batch of books is done. A book nobody
has started is reported as pending, never as a book that yielded nothing.

To regenerate a single book's chunks by hand:

```bash
python 30_SCRIPTS/ingestion/gate_agent_candidates.py chunks \
    --input-file "06_INBOX/Carti/<folder>/<book>.txt" \
    --output-file scratch/<book>_chunks.json
```

`chunks.json` carries the slot **questions**, not just the slot names. Use them.
Asked to place a concept against a bare list of sixteen words, a model picks by
which name sounds closest — that is how "synaptic consolidation" landed in
`procedures`.

## Output format

A JSON list. Each entry:

```json
{
  "chunk_index": 42,
  "concept": "systems consolidation",
  "definition": "…",
  "evidence": "a quote copied verbatim from that chunk",
  "slot": "consolidation",
  "confidence": 0.9,
  "claim_type": "optional"
}
```

Four things that decide whether a candidate survives:

1. **`evidence` must be copied, not recalled.** The gate looks for a 12-word
   verbatim run covering 70% of the quote, in the chunk you named. An accurate
   citation that is absent from the ingested text is worse than no citation: it
   reads as a sound provenance chain and is not.
2. **`chunk_index` must be the chunk you actually read.** Checked against the
   wrong chunk, grounding either passes by luck or fails for the wrong reason, so
   a wrong index is refused before the evidence is examined.
3. **`definition` must not be the evidence reworded.** No shared 8-gram with the
   evidence, token overlap below 0.60. A synonym swap scores 0.68-0.72 and fails.
4. **`slot` must be one of the sixteen.** Nothing is repaired; malformed rows are
   refused.

## Gating

```bash
python 30_SCRIPTS/ingestion/gate_agent_candidates.py gate \
    --input-file "06_INBOX/Carti/<folder>/<book>.txt" \
    --candidates scratch/<book>_candidates.json \
    --source-book "<book>" \
    --output-file staging/<book>.json \
    --rejects-file staging/<book>_rejects.json \
    --agent-label antigravity
```

Rows carry `extraction_method="agent_direct"` and your `--agent-label`, so a later
reader can tell agent output from provider output rather than inferring it.

Read `staging/<book>_rejects.json` before submitting more. A count is not a reason;
the file carries the refused candidate.

## What to expect, and what would be informative

The gates are unchanged from the local-provider path, deliberately. Against a
7-8B model they refused most candidates.

**If a stronger agent's rejection rate does not fall, that is information.** It
means the gates were not calibrated on small-model failure modes and the problem
is elsewhere. The per-reason breakdown is printed for exactly that comparison —
report it.

Ranking: use the `occurrences` column, not `confidence`. Confidence is 1.00 on
nearly everything, including candidates whose evidence turned out to be
fabricated. A floor of `occurrences >= 3` leaves ~160-290 candidates corpus-wide.

## Boundaries

- Do not promote anything out of `06_INBOX/`.
- Do not modify `PROJECT_BRAIN/PROJECT_STATE.md`.
- Do not touch the protected cognitive core (`model_provider.py`,
  `fake_model_provider.py`, `model_tier_router.py`, `actual_usage_telemetry.py`,
  `council_model_execution.py`, `executive_model_execution_bridge.py`).
- Do not loosen a gate to raise the yield. If a gate looks wrong, say so in
  `00_GOVERNANCE/coordination/<your-agent>/CURRENT.md` and leave it.
- Claim your book in `00_GOVERNANCE/coordination/` with an ISO timestamp before
  starting, so two agents do not extract the same one.

## Background

`10_DOCUMENTATION/procedures/Ingesting_A_Book_Into_The_Ontology.md` — the
procedure, including what does not work and two failure shapes.
`07_EVALUATION/book_corpus_conversion/FINDINGS.md` — the measurement log; its
header lists claims that were made and later withdrawn.
