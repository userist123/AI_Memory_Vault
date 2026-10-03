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

The Library-derived `vault_corpus.json` proves that these historical provenance claims exist in the derived corpus. It does not by itself prove that each promotion was performed by PR #206.

## Historical promotion evidence

Repository history independently confirms that a prior ingestion/promotion track made explicit judgments over book-derived concepts.

Commit `6b3c2eacbf8b85135c803800ec1f7d57d93f505f` records the promotion-verdict gate and states that `PROMOTION_REVIEW.md` contained 91 judgments argued from book text:
- 91 total verdicts;
- 48 PROMOTE;
- 28 REJECT;
- 8 MERGE;
- 6 UNSURE;
- 1 SPLIT.

The same historical record states that the verdicts were independently checked against the filesystem, that merge targets existed, and that reasons were required.

Commit `a5d017a96ae8a070b76d589809cbe9a7aa3c973e` contains the same historical promotion-verdict chain as part of the R064 merge-semantics work. This establishes an attributable historical decision process, but it does not by itself establish that every current book-derived record still has complete provenance under today's learning-quality policy.

Commit `6a596beeeac1802590580daeec8ec83bddd61d7b` documents an earlier R032 ingestion/promotion track and records a corpus pre-flight over 20 books, including Kandel, Why We Forget, Soar, and other sources. It also documents a promoter change and graph-relation verification.

These commits are historical evidence only. They do not authorize any lifecycle change in PR #206.

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
