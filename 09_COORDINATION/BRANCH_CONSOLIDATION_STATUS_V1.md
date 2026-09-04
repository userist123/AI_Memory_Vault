# Branch Consolidation Status V1

Date: 2026-09-04
Canonical branch: `main`

## Policy

All substantive project work is performed directly on `main`. Agent work is sequential. Legacy branches are archival references only and receive no new work.

## Selectively preserved on `main`

Historical verified evidence and targeted fixes were selectively copied from legacy branches rather than merging divergent histories wholesale. Preserved items include:

- CODEX R001 forensic evidence: C2, C4, C6, C8, C9, C10, C11, C12, C13, C14, C15 and Wacatac forensic reporting.
- Antigravity A7 associative differential evidence.
- Antigravity A8 production graph differential evidence.
- Weighted graph propagation fix and regression coverage.
- Production storage `all_notes()` graph-indexing contract for SQLite/File storage.
- Context-pack compressed-payload handling and progressive-disclosure budget behavior.
- REVIEW-memory injection regression coverage.
- Cognitive-memory target model and Planning Influence experiment specifications.

## Explicit non-merges

Large divergent branches containing mixed historical README, coordination, audit, code, and knowledge changes are not merged wholesale. This prevents stale branch state from overwriting current `main` governance or implementation.

In particular, `antigravity/observability-v1` is treated as an evidence source, not as a merge candidate.

## Remaining administrative cleanup

The desired end state is a single remote branch named `main`. The current connector exposes branch ref movement but not a remote ref deletion operation. Legacy refs therefore remain until GitHub branch administration can delete them. Their refs must not be used for new development.

## Verification rule

Before any future legacy branch is removed, all unique verified artifacts needed by the project must already exist on `main` with provenance indicating their original branch/commit.
