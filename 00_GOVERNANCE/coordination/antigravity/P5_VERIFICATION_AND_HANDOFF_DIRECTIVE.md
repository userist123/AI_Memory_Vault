# P5 Verification & Handoff Directive

## Owner
Antigravity — retrieval/corpus integration.

## Objective
Convert the reported P5 completion into Git-verifiable evidence at the exact branch head, without changing `main`, rewriting history, or auto-merging PR #17.

## Starting point
Work from the current public head of `antigravity/p1-retrieval-foundation`.
The reported head is `0c956a88ed712fb61baf452a79c9859d88c61375`.
Do not assume earlier P5 reports or short SHA references are valid until resolved against Git.

## Required checks

### 1. Exact-head implementation verification
Verify from the exact current branch head that:
- `MemoryController.search()` no longer invokes `RetrievalEngine.retrieve()`.
- The production path is `MemoryController.search()` -> `request_from_controller()` -> `RetrievalIntegrationAdapter.search()` -> `ProductionRetrievalFacade.retrieve()` -> `RetrievalBoundaryAdapter.retrieve()` -> `HybridRetriever.search()`.
- Principal propagation is preserved end-to-end.
- Cursor/HMAC validation is preserved.
- Adapter/facade security and request-validation errors are translated at the controller boundary.
- Retrieval output is capped at `ACTIVE + verified`.
- Retrieval cache invalidation remains intact across controller mutation paths.

### 2. Test-suite integrity
Inspect the actual committed P5 regression file and reconcile its declared coverage with its real test functions.
The handoff must state the exact number of tests present in the file and the exact command/result actually observed.
Do not claim `227/227` unless the exact command has been executed and the output is preserved as evidence.

### 3. Static audit integrity
Re-run or otherwise reproduce the static retrieval call-path audit against the exact current branch head.
Update `07_EVALUATION/ci_evidence/p5_retrieval_call_path_audit.json` so its `commit` field contains the full exact SHA being audited, not an unresolved short reference.
The audit must distinguish:
- primary production retrieval path;
- test-only direct retriever references;
- authorized boundary construction;
- isolated administrative/audit access;
- cognitive review access.

Do not report `security_bypass_count: 3` while separately enumerating four residual `BYPASS-*` records unless the counting rule is explicitly defined and machine-verifiable.

### 4. Workflow evidence
Resolve the GitHub Actions runs associated with the exact current branch head.
Record only observed statuses/conclusions.
Queued or missing results must remain pending, never be described as green.

### 5. PR synchronization reality
Check PR #17's actual `head_sha` and branch.
Do not describe the PR as synchronized unless GitHub confirms that its head actually points at the intended Antigravity commit or an explicit integration commit.
Because PR #17 is owned by the security branch, do not retarget or merge it from this task.
Prepare a precise handoff that identifies the exact commits that must be reconciled.

### 6. Compatibility boundary
Re-check `memory_controller/consolidation.py` because `consolidate_lessons()` historically queries through `MemoryController.search()` while expecting `REVIEW` notes.
P5 must not silently make that internal workflow inert.
Either:
- preserve a valid secure internal retrieval boundary for that workflow, or
- document a concrete follow-up change required before merge.
Do not weaken public `ACTIVE + verified` retrieval/read security to solve this compatibility issue.

## Required deliverables

1. Any narrowly-scoped code/test/evidence fixes needed to make the P5 implementation and evidence internally consistent.
2. A current exact-head audit JSON.
3. A concise P5 handoff report containing:
   - exact commit SHA;
   - exact production call path;
   - exact test command and observed result;
   - exact CI run IDs/statuses;
   - residual bypass count with defined counting method;
   - SynapseStore external blocker status;
   - consolidation/search compatibility status;
   - explicit statement of what remains pending, if anything.

## Hard constraints
- No modifications to `PROJECT_BRAIN/PROJECT_STATE.md`.
- No modifications to `main`.
- No force-push or history rewrite.
- No auto-merge.
- No invented test or CI evidence.
- Do not rely on short SHAs when an exact SHA can be resolved.

## Completion gate
P5 handoff is considered verified only when the current branch head itself contains the corrected evidence and the handoff report points to exact, Git-resolvable objects and observed results.
