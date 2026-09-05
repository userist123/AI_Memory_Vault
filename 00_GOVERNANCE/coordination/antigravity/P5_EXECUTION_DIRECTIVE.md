# Antigravity P5 Execution Directive

## Objective
Execute and publish the real Phase 5 `MemoryController.search()` production integration on `antigravity/p1-retrieval-foundation`.

## Ground truth
As of 2026-09-05, GitHub verifies this branch at `6572a2bf436d1831fb44144831dcf0b51c16979e` (P4). The previously reported P5 SHAs are not currently resolvable in the repository. Treat the P5 delivery summary as a task specification, not as completed evidence.

## Ownership boundary
Antigravity owns retrieval/corpus integration. Do not modify `PROJECT_BRAIN/PROJECT_STATE.md`. Do not modify ChatGPT-owned runtime-security evidence except where a retrieval integration contract explicitly requires a coordinated note. Do not rewrite history, force-push, or auto-merge.

## Required work

1. Start from the current public branch head and synchronize `memory_controller/controller.py` with the current security/lifecycle contract already present in PR #17 where compatible.
2. Implement the production call path:
   `MemoryController.search()` -> `request_from_controller()` / equivalent controller adapter entry -> `RetrievalIntegrationAdapter.search()` -> `ProductionRetrievalFacade.retrieve()` -> `RetrievalBoundaryAdapter.execute()` -> `HybridRetriever`.
3. Preserve the existing public `MemoryController.search()` response shape, pagination/error semantics, disclosure behavior, audit behavior, and authorization-before-retrieval security invariant.
4. Preserve canonical lifecycle policy behavior. Do not reintroduce compatibility-only lifecycle transition tables.
5. Preserve cache invalidation at every mutation point that can affect retrieval state.
6. Add or update an isolated P5 regression suite covering at minimum:
   - actual adapter invocation from `MemoryController.search()`;
   - no direct legacy `RetrievalEngine.retrieve()` invocation from the production search path;
   - principal propagation and authorization boundary;
   - ACTIVE + verified retrieval ceiling;
   - pagination/cursor preservation;
   - public response/disclosure compatibility;
   - adapter/facade error translation;
   - mutation/cache invalidation compatibility.
7. Add a static retrieval call-path audit proving the legacy bypass is retired. The audit must identify any remaining retrieval references and classify residual ones as isolated/non-primary rather than silently ignoring them.
8. Add a durable P5 report documenting exact full commit SHAs, files changed, call path, security invariants, residual bypasses, test command, and actual observed results.
9. Run the full regression suite and report the exact observed count and duration. Do not state `225/225` unless the repository execution actually produces that result.
10. Ensure GitHub Actions runs for the resulting exact head are visible. A queued workflow is not test evidence.

## Delivery gates
P5 is COMPLETE only when all are true:

- branch head on GitHub contains the integration;
- `MemoryController.search()` no longer calls the legacy retrieval engine directly;
- P5 regression tests exist on the branch;
- the retrieval call-path audit exists and is consistent with the implementation;
- full regression results are actually observed and recorded;
- security invariants remain intact;
- SynapseStore remains explicitly `BLOCKED_BY_EXTERNAL_SYNAPSESTORE` if still unavailable.

## Required final status block

```text
P5 STATUS: COMPLETED | BLOCKED
COMMIT: <full SHA>
BRANCH: antigravity/p1-retrieval-foundation
LEGACY SEARCH PATH: RETIRED | ACTIVE
ADAPTER WIRING: ACTIVE | NOT ACTIVE
TESTS: <observed result>
SECURITY INVARIANTS: <observed result>
RETRIEVAL BYPASSES: <observed residual count/classification>
LIFECYCLE POLICY: CANONICAL | OTHER
SYNAPSE: BLOCKED_BY_EXTERNAL_SYNAPSESTORE | AVAILABLE
```

Do the implementation; do not merely restate the handoff summary.