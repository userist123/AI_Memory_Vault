# P6 Integration Preparation Directive

## Owner
Antigravity — retrieval/corpus integration.

## Objective
Prepare `antigravity/p1-retrieval-foundation` for safe reconciliation into the security workstream after P5 verification. This is preparation only; do not merge PR #17, retarget it, force-push, or modify `main`.

## Current facts
- Antigravity P5 reported head: `d25c0461a3f0be259f21413f350fd5204ed1e25c`.
- PR #17 security head: `3f2417239437a9b16c4d0deb0e08d6d5f59e765b`.
- The branches diverge from merge base `32889fdadbcdd069af360e246b68c4f4a05e4813`.
- Git comparison shows the Antigravity branch contains the retrieval/P5 changes plus the security workstream commits.
- Git comparison also shows a tracked binary cache artifact: `cognitive_core/__pycache__/__init__.cpython-314.pyc`.

## Required work

### 1. Remove generated artifacts
Remove the tracked `cognitive_core/__pycache__/__init__.cpython-314.pyc` and verify no generated `__pycache__`/`.pyc` artifacts remain in the intended integration delta.
Do not delete legitimate source files.

### 2. Re-verify P5 source integrity
At the resulting exact head, verify:
- `MemoryController.search()` delegates through the production adapter chain.
- Public retrieval remains capped at `ACTIVE + verified`.
- `consolidate_lessons()` uses the authorized internal query path for `REVIEW` lessons and does not weaken public retrieval.
- Cache invalidation remains intact after controller mutations.
- Cursor/HMAC and error translations remain intact.

### 3. Regression evidence
Run and preserve exact observed results for:
- `python -m pytest 20_TESTS/regression/test_p5_controller_search_integration.py -q`
- `python -m pytest 20_TESTS/regression/ -q`
- the repository's standard Memory V6 workflow scope if locally available.
Do not infer CI success from local success. Do not claim green GitHub Actions unless GitHub reports a completed successful run for the exact head.

### 4. Audit/evidence consistency
Update `07_EVALUATION/ci_evidence/p5_retrieval_call_path_audit.json` so:
- `commit` is the exact full SHA of the resulting head;
- bypass counting is mechanically defined and consistent with the enumerated `BYPASS-*` records;
- test-only direct retriever references are separated from production access;
- no stale short SHA remains in the evidence.

### 5. Exact-head handoff
Update/create a concise handoff report containing:
- exact resulting SHA;
- production call path;
- exact P5 test count/result;
- exact full regression result;
- exact GitHub Actions run IDs and observed states/conclusions;
- residual bypass count and counting rule;
- consolidation compatibility result;
- SynapseStore external blocker;
- explicit pending items, especially any CI still queued/pending.

### 6. Integration handoff
Record the exact commit range that the security branch must reconcile. Do not modify PR #17 itself from this task. Do not declare synchronization until GitHub confirms the actual PR head.

## Hard constraints
- No `PROJECT_BRAIN/PROJECT_STATE.md` changes.
- No changes to `main`.
- No force-push/history rewrite.
- No auto-merge.
- No invented empirical or CI evidence.
- Do not weaken public `ACTIVE + verified` security to support internal workflows.

## Completion gate
The task is complete only when the resulting branch head is clean of generated artifacts, P5 evidence references that exact head, and the handoff identifies the precise reconciliation range for the security branch.
