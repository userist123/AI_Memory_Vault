# Work order: read the corpus yourself

**For:** Antigravity (also valid for Codex, ChatGPT)
**Issued:** 2026-09-11 · **Issued by:** Claude Code (r036)

## The one instruction that matters

**You read the passages. Not a model you call.**

Do not write a script that sends the chunks to a language model. Do not start
Ollama, LM Studio, llama.cpp, or any local server. Do not spawn a subagent whose
job is to call one. The extraction is the reading, and the reading is yours.

This is not a preference about tooling. It has been tried:

| | result |
|---|---|
| 7 of 20 books, `llama3.1:8b` | 82 candidates kept, **1** reached the review floor |
| top rejection | `verbatim_ngram` ×33 — the definition was the quote reworded |
| next | `evidence_not_in_source` ×18 — the citation was invented |
| Ashby, *Introduction to Cybernetics*, 53 sections | **0 candidates** |

That run was stopped. You are not being asked to repeat it faster.

## What is already done for you

`scratch/agent_corpus/` holds all 1,120 chunks, one `<name>_chunks.json` per
book, and `manifest.json` with the order. Nothing needs converting.

Work in the order `manifest.json` gives. It is largest-book-first, because
`occurrences` — how many distinct sections define the same term — is the only
ranking signal here that carries information, and it cannot be measured on a
book with fewer sections than the review floor of 3. The previous run went
smallest-first and produced five consecutive books whose zero was arithmetic.

Each chunks file carries the sixteen slot **questions**. Use them. Against a
bare list of sixteen names an extractor picks by which name sounds closest;
that is how "synaptic consolidation" was once filed under `procedures`.

## What you write back

`scratch/agent_corpus/<name>_candidates.json` — a JSON list:

```json
{
  "chunk_index": 42,
  "concept": "systems consolidation",
  "definition": "…",
  "evidence": "a span copied verbatim out of that chunk",
  "slot": "consolidation",
  "confidence": 0.9
}
```

- `evidence` is **copied, not recalled**. The gate looks for a 12-word verbatim
  run covering 70% of it, inside the chunk you named.
- `chunk_index` is the chunk you actually read. Checked against the wrong one,
  grounding passes by luck or fails for the wrong reason.
- `definition` is not the evidence reworded: no shared 8-gram, token overlap
  under 0.60. A synonym swap scores 0.68–0.72 and is refused.
- `slot` is one of the sixteen. Nothing is repaired; malformed rows are refused.

## Gating

```bash
python 30_SCRIPTS/ingestion/run_agent_corpus.py collect \
    --work-dir scratch/agent_corpus --agent-label antigravity
```

Resumable — run it whenever a batch is done. Read
`staging/<name>_rej.json` before submitting more; it carries the refused
candidate, not just a count.

## What the report will say about you

Every row this produces is written `provider: "agent"`. That label is worth
exactly what you actually did, and no instruction in this file can enforce it.

So `collect` probes the local model ports and writes what it finds into the
report, beside the numbers: which servers answered, and what they had loaded.
It does not block anything — a clean probe is weak evidence, since work could
have gone through a remote endpoint, and a false positive should not stop a
run. It exists so the label can be weighed instead of taken.

If you do end up using a model for some part of this, **say so in the run
notes.** A stated method can be discounted correctly. An unstated one poisons
every number downstream of it.

## Boundaries

- Nothing is promoted out of `06_INBOX/`.
- Do not modify `PROJECT_BRAIN/PROJECT_STATE.md`.
- Do not touch the protected cognitive core (`model_provider.py`,
  `fake_model_provider.py`, `model_tier_router.py`, `actual_usage_telemetry.py`,
  `council_model_execution.py`, `executive_model_execution_bridge.py`). A
  vendor-named provider class under that Protocol needs a decision first — the
  Protocol is provider-neutral on purpose.
- Do not loosen a gate to raise the yield. If a gate looks wrong, write it in
  `00_GOVERNANCE/coordination/antigravity/CURRENT.md` and leave the gate alone.
- Claim each book in `CURRENT.md` with an ISO timestamp before starting.

## If your rejection rate does not fall

That is a finding, and it is the one worth reporting. The thresholds were
calibrated against measured populations, not against a small model's
weaknesses: apparent fabrications ran 3–6 verbatim words against 25–27 for real
quotes. If a capable reader is refused at the same rate, the gates are wrong
about something and the per-reason breakdown is where it shows. Report it
rather than working around it.

## Background

- `00_GOVERNANCE/coordination/BOOK_INGESTION_HANDOFF.md` — the general brief
- `10_DOCUMENTATION/procedures/Ingesting_A_Book_Into_The_Ontology.md` — the
  procedure, including what does not work
- `07_EVALUATION/book_corpus_conversion/FINDINGS.md` — the measurement log; its
  header lists claims that were made and later withdrawn
