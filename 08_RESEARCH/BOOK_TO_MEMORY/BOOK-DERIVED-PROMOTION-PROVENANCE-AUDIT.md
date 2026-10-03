# Book-Derived Promotion Provenance Audit

Date: 2026-10-03
PR: #206
Scope: research/governance only

## Finding

The Book-to-Memory research maps in this PR explicitly state that source-derived concepts are research candidates and that no ACTIVE promotion is performed.

The existing derived Vault corpus, however, contains historical records whose metadata includes `promoted` status and/or `ACTIVE` lifecycle while naming Book-to-Memory sources such as:
- Minsky / Society of Mind
- Wiener / cybernetics
- Ashby / Design for a Brain
- Kandel / molecular biology of memory
- Schacter & Tulving / Memory Systems
- Why We Forget
- Soar / 7688_jkt_au
- Newell / Unified Theories of Cognition
- AI-agent memory survey sources

The search evidence is from the Library's derived `vault_corpus.json`, not from the current Raw Inbox source snapshots. Therefore it proves that the derived corpus contains these historical provenance claims, but it does not prove that each promotion was performed by PR #206.

## Why this matters

This creates a provenance boundary that must remain explicit:

`source text -> research map -> engineering hypothesis -> experiment -> validated mechanism`

must not be silently collapsed into:

`source text -> ACTIVE memory`.

A historical `promoted` field is not equivalent to current empirical validation.

## Decision

Do **not** automatically demote, delete, or rewrite the historical records from this research PR.

Doing so could:
- alter current Vault semantics;
- destroy historical provenance;
- invalidate downstream references;
- hide whether the original promotion was governed by a different policy/version.

## Required reconciliation

A dedicated provenance-reconciliation task should determine, for every book-derived promoted concept:

1. original source snapshot and source hash;
2. exact source/page evidence;
3. promotion commit and actor;
4. governing policy/version at promotion time;
5. whether the promotion was empirical, architectural, or merely bibliographic;
6. current lifecycle;
7. current production dependency;
8. whether revalidation is required under current learning-quality policy.

Classify each record as:
- `VALIDATED`
- `HISTORICAL_UNVERIFIED`
- `PROVENANCE_MISSING`
- `REQUIRES_REVALIDATION`

No classification should itself change lifecycle.

## Research isolation

PR #206 does not promote or demote these records. Its experiments must treat the historical corpus state as an input condition and must not use historical promotion metadata as evidence that a cognitive/biological claim is experimentally validated.

## Next experiment

After provenance reconciliation, compare retrieval/behavior with and without the historical book-derived records only if a preregistered experiment requires it. Record corpus identity and hash, and keep any resulting benchmark separate from the production baseline.

## Acceptance condition

This audit is complete only when each book-derived promoted record has an attributable promotion event or is explicitly marked as provenance-missing and queued for owner-controlled reconciliation.

No production change is authorized by this document.
