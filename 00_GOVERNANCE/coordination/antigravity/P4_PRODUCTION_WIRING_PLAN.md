# P4 Production Wiring Plan

**Agent**: Antigravity  
**Branch**: `antigravity/p1-retrieval-foundation`  
**P3 Baseline Commit**: `0e9390c0c624b57cfdedaa4247ab7316b5be7dc7`  
**Date**: 2026-09-05  
**Document Purpose**: Definitive technical specification and execution runbook for activating production retrieval wiring in `MemoryController.search()`.

---

## 1. Readiness Classification

| Subsystem / Capability | Readiness State | Rationale |
| :--- | :--- | :--- |
| **Retrieval Integration Adapter** | **READY TO WIRE** | Fully verified, pass 45/45 matrix tests, sub-millisecond latency, zero-mutation. |
| **Security Invariant Enforcement** | **READY TO WIRE** | All 8 Security Invariants pass 18/18 explicit regression tests. |
| **Runtime Wiring Harness & Shims** | **READY TO WIRE** | Tested and verified in `cognitive_core/p4_runtime_wiring_harness.py`. |
| **Corpus Quality Gate** | **CONDITIONAL_PASS** | Remediation and duplicate clustering complete; 580 review drafts isolated outside active boundary. |
| **Production Controller Modification** | **DEFERRED (READY UPON CALL)** | Intentionally held in P4 to maintain zero disruption to ChatGPT lifecycle workstream. |
| **External SynapseStore** | **BLOCKED** | External `05_DATA/synapses.json` and `cognitive_core/synapse_store.py` are absent (`BLOCKED_BY_EXTERNAL_SYNAPSESTORE`). |

---

## 2. File and Function Targeting

### File to Modify
`memory_controller/controller.py`

### Functions to Modify
1. `MemoryController.__init__(...)`:
   - Initialize `VaultIndex.load(...)` (or inject an existing vault instance).
   - Instantiate `HybridRetriever(vault)`.
   - Instantiate `RetrievalBoundaryAdapter(retriever)`.
   - Instantiate `ProductionRetrievalFacade(boundary)`.
   - Instantiate `RetrievalIntegrationAdapter(facade)`.
   - Store as `self.retrieval_adapter`.

2. `MemoryController.search(...)`:
   - Replace the legacy call path:
     ```python
     notes = self.retrieval_engine.retrieve(classified, principal, query_fp, disclosure_level, budget, offset=offset)
     scored = self.scorer.score(sanitized, notes)
     ```
   - Wire directly to `RetrievalIntegrationAdapter`:
     ```python
     req = request_from_controller(
         principal=principal,
         query=sanitized,
         page_size=page_size,
         page_token=page_token,
         lifecycles=lifecycles,
         types=types,
         disclosure_level=disclosure_level,
         request_id=target_id,
     )
     adapter_resp = self.retrieval_adapter.search(req)
     pack = response_to_controller(adapter_resp, budget=budget, original_disclosure_level=disclosure_level)
     ```

---

## 3. Legacy Code to Replace

### In `MemoryController.search()`:
The existing search implementation performs an ad-hoc query through `self.retrieval_engine.retrieve()` which directly queries `self.storage.query()`. This legacy path:
- Does not enforce `verification == "verified"`.
- Uses ad-hoc HMAC tokens rather than multi-factor principal-bound HMAC cursors.
- Lacks graph-assisted retrieval and multi-hop discovery.

### Replacement Diff (Conceptual Phase 5 Execution)
```diff
--- a/memory_controller/controller.py
+++ b/memory_controller/controller.py
@@ -240,43 +240,16 @@ class MemoryController:
-            notes = self.retrieval_engine.retrieve(classified, principal, query_fp, disclosure_level, budget, offset=offset)
-            scored = self.scorer.score(sanitized, notes)
-            score_map = {s['id']: s['score'] for s in scored}
-            notes = sorted(notes, key=lambda n: score_map.get(n.get('id'), 0), reverse=True)
-            pd = ProgressiveDisclosure(budget)
-            ...
-            pack = self.pack_builder.build(...)
+            req = request_from_controller(
+                principal=principal,
+                query=sanitized,
+                page_size=page_size,
+                page_token=page_token,
+                lifecycles=lifecycles,
+                types=types,
+                disclosure_level=disclosure_level,
+                request_id=target_id,
+            )
+            adapter_resp = self.retrieval_adapter.search(req)
+            pack = response_to_controller(adapter_resp, budget=budget, original_disclosure_level=disclosure_level)
```

---

## 4. Contract Transition

1. **Input Interface**: `MemoryController.search()` retains its exact signature, guaranteeing backward compatibility for all existing callers.
2. **Output Interface**: The returned structure remains a dictionary containing `requestId`, `agentId`, `budget`, `disclosureLevel`, `results`, and `next_page_token`.
3. **Security Invariant Guarantee**: Callers can no longer retrieve notes outside `ACTIVE + verified`. Any attempt to pass `lifecycles=["REVIEW"]` or `lifecycles=["RAW"]` will trigger `PermissionError` fail-closed.

---

## 5. Test Suites Required to Remain Green

During and after production wiring, the following test suites must pass 100%:
1. `memory_controller/tests/test_authorization.py` (all authorization checks)
2. `memory_controller/tests/test_audit.py` (tamper-evident audit trail logging)
3. `memory_controller/tests/test_cache.py`
4. `memory_controller/tests/test_core.py`
5. `20_TESTS/regression/test_retrieval_foundation.py`
6. `20_TESTS/regression/test_retrieval_boundary.py`
7. `20_TESTS/regression/test_retrieval_facade.py`
8. `20_TESTS/regression/test_retrieval_integration_adapter.py`
9. `20_TESTS/regression/test_p4_runtime_integration_matrix.py`
10. `20_TESTS/regression/test_p4_security_invariants.py`
11. `20_TESTS/regression/test_p4_runtime_wiring_harness.py`

---

## 6. Security Invariants Preserved

- **INVARIANT 1**: No principal can retrieve beyond `ACTIVE + verified`.
- **INVARIANT 2**: No cursor can cross principal boundaries.
- **INVARIANT 3**: No filter can broaden the security ceiling.
- **INVARIANT 4**: No adapter request can mutate filesystem/database state.
- **INVARIANT 5**: No adapter response exposes raw retriever internals.
- **INVARIANT 6**: Pagination preserves the exact security envelope of page 1.
- **INVARIANT 7**: Ordering remains deterministic under identical requests.
- **INVARIANT 8**: Pre-retrieval early fail-closed rejection.

---

## 7. Rollback & Fail-Safe Strategy

To ensure zero downtime or unexpected regression:
1. **Feature Flag / Environment Switch**:
   Introduce `MEMORY_RETRIEVAL_BACKEND` configuration setting:
   - `adapter` (default in Phase 5): routes through `RetrievalIntegrationAdapter`.
   - `legacy`: routes through legacy `RetrievalEngine`.
2. **Git Atomic Revert**:
   Wiring will be performed in a single atomic commit on `antigravity/p1-retrieval-foundation`. If any unexpected regression occurs, a clean `git revert` can be performed in $<1$ minute.

---

## 8. Migration Risks & Mitigation

| Risk | Severity | Mitigation |
| :--- | :--- | :--- |
| **Existing tests requesting unverified or REVIEW notes** | Medium | The P4 test matrix already validates fail-closed behavior; legacy tests expecting unverified data in `search()` must be redirected to `query()` or updated to expect `PermissionError`. |
| **Cursor format difference** | Low | New cursors are issued by `RetrievalIntegrationAdapter`. Existing in-flight cursors from before the deployment expire within 10 minutes. |
| **Performance overhead** | Negligible | Adapter benchmark demonstrates sub-millisecond latency ($0.99$ ms median). |

---

## 9. Dependency on PR #17 & SynapseStore

- **PR #17 Alignment**: ChatGPT is finalizing SQLite transactions, mutation atomicity, and verification state transitions. The `RetrievalIntegrationAdapter` was built to be completely decoupled from SQLite storage internals, consuming only the standardized `VaultIndex` abstraction.
- **SynapseStore Blocker**: Status is `BLOCKED_BY_EXTERNAL_SYNAPSESTORE`. As demonstrated in P1.4–P3, the `CorpusGraph` and `HybridRetriever` function with high precision using the vault's entity-link graph, without requiring the external file `05_DATA/synapses.json`. When external `synapse_store.py` becomes available, it can be plugged into `CorpusGraph` without altering the `RetrievalIntegrationAdapter` contract.
