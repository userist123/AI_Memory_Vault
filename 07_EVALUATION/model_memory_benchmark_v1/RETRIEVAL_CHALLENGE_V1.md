# Retrieval Challenge V1 — executed against remote main

Date: 2026-09-04
Evaluator: GPT-5.6 Luna
Baseline commit: `75797ead1e276337ac472398451a1f154bf84396`

## Purpose

This benchmark tests whether the newly consolidated book knowledge is actually addressable by the repository's production retrieval path. It is independent of Ollama/model execution.

## Test corpus

Ten synthesis atoms were selected as gold targets: `M-ADAPT-001`, `M-ARCH-001`, `M-DISTRIBUTED-001`, `M-EVAL-001`, `M-LEARNING-001`, `M-RELIABILITY-001`, `M-REPRESENT-001`, `M-RETRIEVAL-001`, `M-TOOLS-001`, and `M-TRADEOFF-001`.

Twenty paraphrased positive queries were constructed, two per target. Ten negative queries were constructed for concepts outside the ten-target set.

## Critical execution finding

The gold synthesis atoms are stored in `06_INBOX/DERIVED/BOOKS/2026-09-04/consolidated/knowledge_atoms.jsonl`.

The production `FileStorageEngine` explicitly excludes `06_INBOX` from its index and only indexes `00_CORE`, `01_KNOWLEDGE`, `02_PROJECTS`, `03_PROCEDURES`, `04_MEMORY`, `05_RESOURCES`, and `99_SYSTEM`. Therefore the ten gold synthesis IDs are not addressable by the production FileStorageEngine search surface.

A direct GitHub code search for `M-ADAPT-001` also returned zero repository hits outside the derived artifact itself, confirming there is no canonical duplicate of that ID.

## Positive-query result

Because every positive gold target is outside the retrieval index, the production retrieval path cannot return any of the ten gold IDs.

- Positive cases: 20
- Gold targets reachable through production FileStorageEngine: 0/20
- Recall@5: **0.0000**
- MRR: **0.0000**
- Target addressability: **0%**

This is a deterministic architectural result, not a simulated model score.

## Negative-query behavior

The retrieval engine has a second structural issue: it retrieves a candidate set from storage before relevance scoring and does not apply a minimum relevance threshold. The scorer only changes ordering. Consequently, when canonical storage is non-empty, an unrelated query can still receive up to five results.

Therefore the current implementation has no true empty-result discrimination gate. A precise false-positive percentage requires executing against the full local corpus, so this benchmark records the invariant but does not invent a numeric FP rate.

## Root cause

The book consolidation pipeline correctly keeps these atoms human-gated, but the current architecture leaves them in `06_INBOX/DERIVED/...`, while production retrieval intentionally excludes `06_INBOX`.

This creates a lifecycle integration gap:

`RAW/DERIVED book knowledge -> synthesis -> human review`

exists, but:

`reviewable synthesis -> cognitive retrieval`

is not yet wired.

## Verdict

**FAIL — BOOK KNOWLEDGE IS NOT RETRIEVABLE THROUGH THE PRODUCTION MEMORY PATH.**

This does not mean the knowledge should be auto-promoted. The correct fix is a controlled cognitive-review retrieval surface that can expose `REVIEW` material with explicit `_cognitive_unverified` semantics, while keeping canonical `read()` and promotion boundaries intact.

## Required next engineering gate

1. Expose human-review synthesis atoms to the cognitive retrieval path without treating them as canonical ACTIVE memory.
2. Preserve provenance and `verification_required=true`.
3. Add query aliases/keywords and semantic relations for the atoms.
4. Add an explicit relevance threshold or abstention behavior for unrelated queries.
5. Re-run this challenge with actual top-5 IDs and compute Precision@5, Recall@5, MRR and false-positive rate.
6. Do not promote any atom automatically as part of this fix.
