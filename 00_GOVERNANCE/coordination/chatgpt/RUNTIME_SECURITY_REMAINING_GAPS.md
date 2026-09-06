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
- Public `MemoryController.read()` now requires both `ACTIVE` lifecycle and `verified` verification state.
- Supersession boundary requires an `ACTIVE` predecessor.
- Frontmatter schema accepts `RECONSOLIDATING` with regression coverage.

## Remaining architecture work

### 1. Repository-wide write-path inventory

The guarded controller paths are not, by themselves, proof that every repository write path obeys the same authority. Direct storage writes in importers, scripts, helpers, background code, or other components must be inventoried and either routed through canonical boundaries or explicitly classified as trusted infrastructure. The repository scanner now also detects common filesystem mutation APIs, and a dedicated GitHub Actions workflow executes that inventory against the exact PR revision and uploads machine-readable evidence.

### 2. Proposal lifecycle boundary is not yet fully uniform across principals

`MemoryController.propose()` currently constrains caller-supplied creation lifecycle values explicitly for `AI_AGENT`, while `HUMAN`/`ADMIN` can still supply privileged creation lifecycle values that are not part of the permitted creation set. This is a remaining fail-closed policy gap and should be closed before merge, ideally with a principal-independent creation-state ceiling plus regression coverage.

### 3. Read-path lifecycle/verification semantics need end-to-end validation

Public `read()` is now explicitly `ACTIVE + verified`, while `cognitive_read()` permits `ACTIVE` and `REVIEW` and marks `REVIEW` as unverified. Standard search still needs one end-to-end acceptance matrix across all public read surfaces. Financial search has an explicit `ACTIVE + verified` ceiling and regression coverage.

### 4. Retrieval production integration remains deferred to Antigravity

Antigravity owns retrieval/corpus integration. The public `antigravity/p1-retrieval-foundation` branch was verified at P4 when the reported P5 delivery was checked, while the claimed P5 commit SHAs were not resolvable at that time. The P5 execution directive requires the actual controller wiring, call-path audit, isolated regression suite, and observed full-suite results before P5 is accepted as complete.

### 5. Final CI evidence is still pending

Queued/pending workflows are not evidence of a green build. The security branch requires complete CI results before merge readiness can be declared.

### 6. External SynapseStore dependency

`05_DATA/synapses.json` and `cognitive_core/synapse_store.py` remain an external dependency according to Antigravity P4. Native corpus-graph fallback remains the verified path; no mock SynapseStore data is introduced by this security branch.

## Non-goals for this document

- No retrieval ranking changes.
- No graph/synapse changes.
- No `PROJECT_BRAIN/PROJECT_STATE.md` changes.
- No direct merge of the historical security branch.


## 🔗 Legături Sinaptice
- [[00_GOVERNANCE/README|Governance]]
- [[00 Core Map]]
- [[14 Subagents Council Map]]
- [[Knowledge Graph Home]]
