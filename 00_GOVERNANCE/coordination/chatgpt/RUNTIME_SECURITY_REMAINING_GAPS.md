# Runtime Security — Remaining Gaps

## Scope

This document records runtime trust-boundary gaps identified during the security/lifecycle closure work on `runtime-security-lifecycle-closure`.

## Closed in current branch

- Reconsolidation challenge/resolve authorization with explicit principal enforcement.
- Reconsolidation lifecycle transitions checked through the canonical lifecycle policy.
- Direct financial-ingestion authorization and lifecycle/verification canonicalization.
- Financial search now authorizes the caller up front and exposes only `ACTIVE + verified` records as defense-in-depth read filtering.
- SQLite `RECONSOLIDATING` schema support and migration coverage.
- Canonical lifecycle policy introduced for mutation semantics.
- Legacy pipeline transitions now route through the canonical policy (`CLASSIFY`, `NORMALIZE`, `VERIFY`, `PROMOTE`) from `MemoryController._validate_note()`.
- Controller mutation paths route `review`, `promote`, `archive`, and `supersede` through the canonical policy.
- `MemoryController.search()` authorizes `Operation.SEARCH` before any query/retrieval work.
- Supersession boundary requires an `ACTIVE` predecessor.
- Frontmatter schema accepts `RECONSOLIDATING` with regression coverage.

## Remaining architecture work

### 1. Repository-wide write-path inventory

The guarded controller paths are not, by themselves, proof that every repository write path obeys the same authority. Direct storage writes in importers, scripts, helpers, background code, or other components must be inventoried and either routed through canonical boundaries or explicitly classified as trusted infrastructure. The repository scanner now also detects common filesystem mutation APIs; exact-commit execution evidence is still required.

### 2. Read-path lifecycle/verification semantics need end-to-end validation

Public `read()` is ACTIVE-only while `cognitive_read()` permits ACTIVE and REVIEW and marks REVIEW as unverified. Standard search still needs one end-to-end acceptance matrix across all public read surfaces. Financial search has an explicit `ACTIVE + verified` ceiling and regression coverage.

### 3. Retrieval production integration remains deferred

Antigravity's retrieval facade is intentionally production-unwired on its own branch until the controller wiring change is integrated. Phase 5 is now authorized after P4 readiness, but merge order and compatibility evidence must remain explicit.

### 4. Final CI evidence is still pending

Queued/pending workflows are not evidence of a green build. The latest branch head requires complete CI results before PR merge readiness can be declared.

### 5. External SynapseStore dependency

`05_DATA/synapses.json` and `cognitive_core/synapse_store.py` remain an external dependency according to Antigravity P4. Native corpus-graph fallback remains the verified path; no mock SynapseStore data is introduced by this security branch.

## Non-goals for this document

- No retrieval ranking changes.
- No graph/synapse changes.
- No `PROJECT_BRAIN/PROJECT_STATE.md` changes.
- No direct merge into `main`.
