# Canonical Memory Data Plane Implementation Plan

**Goal:** Establish one explicit routing seam for every model-facing memory context path while keeping retrieval, reduction, security enforcement, and model egress as separate responsibilities.

**Architecture:** `MemoryDataRouter` owns a registry of approved model-facing memory routes and dispatches requests to their producers. Producers perform retrieval and ContextPack construction but do not perform final model egress. `MemoryDataEgressGate` validates verification, provenance, security budget, token-economy metadata, and final model-facing token limits immediately before return.

**Global constraints:**
- Retrieval remains independent from semantic reduction.
- Unverified or blocked content must never reach model-facing context.
- Provenance is mandatory for model-facing results.
- Token economy must not remove protected security, provenance, requirements, acceptance, or mandatory content.
- If protected verified context cannot fit the hard budget, the path fails closed.
- Routing must not silently become retrieval, ranking, compression, or authorization.
- Model-facing producers must use the canonical router and must not call the egress gate directly.
- Financial retrieval must remain routable when called directly.
- ToolRouter, AgentRole routing, lifecycle routing, and write boundaries remain separate responsibilities.

## Implemented tasks
- [x] Separated `MemoryDataRouter` from `MemoryDataEgressGate`.
- [x] Added explicit route registration and dispatch.
- [x] Routed `read`, `cognitive_read`, and `search` through the controller router.
- [x] Routed direct financial `execute_search()` through the canonical router.
- [x] Removed direct egress calls from retrieval producers.
- [x] Added tests for dispatch, rejection, missing routes, and direct-egress bypasses.

## Verification
Run `python -m pytest security/tests/test_canonical_memory_data_route.py -q`.
GitHub Actions remains authoritative for the current PR head. The implementation is not considered passing until the current Security Boundary Tests, Memory Data Route Gate, Context Token Economy Gate, and applicable security checks complete successfully.