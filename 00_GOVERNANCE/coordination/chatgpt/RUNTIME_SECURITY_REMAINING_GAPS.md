# Runtime Security — Remaining Gaps

## Scope

This document records runtime trust-boundary gaps identified during the security/lifecycle closure work on `runtime-security-lifecycle-closure`.

## Closed in current branch

- Reconsolidation challenge/resolve authorization with explicit principal enforcement.
- Reconsolidation lifecycle transitions checked through the canonical lifecycle policy.
- Direct financial-ingestion authorization and lifecycle/verification canonicalization.
- SQLite `RECONSOLIDATING` schema support and migration coverage.
- Canonical lifecycle policy introduced for mutation semantics.
- Legacy pipeline transition types are now explicitly represented in the canonical policy (`CLASSIFY`, `NORMALIZE`, `VERIFY`, `PROMOTE`).
- Controller mutation paths route `review`, `promote`, `archive`, and `supersede` through the canonical policy.
- Supersession boundary requires an `ACTIVE` predecessor.
- Frontmatter schema accepts `RECONSOLIDATING` with regression coverage.

## Remaining architecture work

### 1. Wire legacy pipeline transitions to the canonical policy

The canonical policy now defines the historical pipeline transitions `RAW -> CLASSIFIED`, `CLASSIFIED -> NORMALIZED`, `REVIEW -> VERIFIED`, and `VERIFIED -> ACTIVE`. The remaining step is to replace the controller's compatibility-only transition branch with the corresponding named mutations, while preserving existing callers and verification semantics.

### 2. Repository-wide write-path inventory is still required

The guarded controller paths are not, by themselves, proof that every repository write path obeys the same authority. Direct storage writes in importers, scripts, helpers, background code, or other components must be inventoried and either routed through canonical boundaries or explicitly classified as trusted infrastructure.

### 3. Read-path lifecycle/verification semantics need end-to-end validation

Public `read()` is ACTIVE-only while `cognitive_read()` permits ACTIVE and REVIEW and marks REVIEW as unverified. Standard search and financial search have additional retrieval/filtering behavior. These contracts need one end-to-end acceptance matrix so lifecycle and verification exposure is consistent across every read path.

### 4. Retrieval production integration remains deferred

Antigravity's retrieval facade is intentionally production-unwired. Its integration must remain a separate phase after retrieval readiness, corpus-quality evidence, and runtime security acceptance are stable.

### 5. Final CI evidence is still pending

Queued/pending workflows are not evidence of a green build. The latest branch head requires complete CI results before PR merge readiness can be declared.

## Non-goals for this document

- No retrieval ranking changes.
- No graph/synapse changes.
- No `PROJECT_BRAIN/PROJECT_STATE.md` changes.
- No direct merge into `main`.
