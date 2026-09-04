# CODEX R001 — C9 Production Graph Integration

Status: DISPATCHED
Owner: CODEX
Base: current `main` at dispatch time
Priority: P0

## Objective

Resolve the confirmed production-path disconnect between graph/ranked retrieval and the real SQLite/File storage-backed `MemoryController` path, then prove the fix with reproducible tests.

## Ground truth to start from

- Do not assume the previous in-memory graph demo represents production behavior.
- Re-read current `main` before changes.
- Preserve all lifecycle, provenance, authority and security boundaries.
- The known production issue is that `ranked_search.py` has behavior that can depend on a `.store` capability that real production storage engines do not expose; prior handling could swallow the failure and return raw results.
- `ContextPackBuilder` may also discard `relevance_score`, causing downstream ranking to reconstruct synthetic seeds. Verify this against current code rather than assuming it remains unchanged.

## Required work

1. Reproduce the production-path graph disconnect on current `main`.
2. Define the smallest safe architectural fix that makes graph/ranked retrieval operate through supported production storage interfaces.
3. Implement the fix without weakening security, lifecycle, provenance or authority controls.
4. Preserve deterministic fallback behavior when graph/ranking infrastructure is unavailable; no silent exception swallowing that falsely claims graph success.
5. Ensure weighted graph edges remain semantically effective; do not regress the earlier edge-weight repair.
6. Add focused regression tests covering:
   - production SQLite/File storage path;
   - ranked retrieval with graph enabled;
   - graph-disabled deterministic baseline;
   - unsupported graph/storage capability behavior;
   - relevance-score preservation into downstream ranking;
   - edge-weight influence where applicable.
7. Run the targeted tests and the relevant broader regression suite locally.
8. Record exact command lines and real stdout/stderr, test counts, duration and failures/skips.
9. Commit every substantive change.
10. Push the branch and provide the exact remote branch/HEAD SHA.
11. Produce `07_EVALUATION/codex/R001_C9_PRODUCTION_GRAPH_INTEGRATION.md` with:
   - baseline SHA;
   - reproduction evidence;
   - root cause;
   - changed files;
   - architecture rationale;
   - tests and real outputs;
   - limitations/unverified claims;
   - remote verification data.

## Acceptance criteria

C9 is complete only when all are true:

- Production storage path no longer silently bypasses graph/ranked retrieval due to an interface mismatch.
- Graph-enabled production retrieval is behaviorally demonstrated with a real production storage implementation.
- Existing deterministic retrieval remains intact and regression-tested.
- Edge weights remain effective and regression-tested.
- No REVIEW→ACTIVE promotion, security-policy weakening, benchmark manipulation or provenance loss.
- Evidence distinguishes CODE_VERIFIED / TEST_VERIFIED / RUNTIME_VERIFIED / CI_VERIFIED / UNVERIFIED claims.
- Remote branch and commit SHA are independently verifiable.

## Important boundary

This task is implementation, not a claim that memory is causally beneficial. Do not rewrite benchmark definitions or infer causal usefulness from the graph repair alone.
