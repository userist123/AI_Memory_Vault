# CODEX R001 — NEXT TASK C9

## Mission
Repair the **production graph/activation integration** exposed by Antigravity GAP-012, but only after C8 held-out retrieval evidence is captured. This is a production-path repair task, not a report-only task.

## Start from
Resolve the actual `origin/main` SHA before work. Do not assume any historical baseline.

## Dependencies
- C8 held-out retrieval results must be recorded first.
- Reuse the current validated edge-weight fix only if it is present on the working baseline; otherwise treat it as a separate branch change and document ancestry.

## Target defects
1. `ranked_search.py` / `build_multi_graph()` must not depend on a test-only `.store` attribute.
2. Production `SQLiteStorageEngine` and `FileStorageEngine` must have a valid graph input path if graph reranking is intentionally supported.
3. `relevance_score` must survive context construction into any downstream reranking stage that actually consumes it.
4. Broad `except Exception` fallback must not silently turn graph failure into indistinguishable base retrieval.

## Required behavior
- Preserve lifecycle/security invariants.
- Preserve deterministic fallback when graph integration is unavailable.
- Fail observably: trace the graph stage as `AVAILABLE`, `UNAVAILABLE`, or `FAILED`, with reason.
- Never fabricate graph candidates.
- Never silently promote REVIEW.
- Do not redesign the whole retrieval architecture.

## Implementation rules
1. Prove each defect on current main before fixing it.
2. Make the smallest production-path repair that removes the demonstrated blocker.
3. Add regression tests for both SQLite and File storage paths.
4. Add tests proving graph failure cannot masquerade as successful graph reranking.
5. Add a test proving the propagated relevance score is preserved into the consumer path.
6. Re-run C8's held-out suite after the repair; do not redefine its corpus or metrics.
7. Run targeted tests plus full regression.
8. Verify `git diff --check` and remote commit.

## Evidence required
`07_EVALUATION/codex/C9_PRODUCTION_GRAPH_INTEGRATION.md`

Must contain:
- BASE_COMMIT
- defect reproduction commands + raw stdout/stderr
- exact failing production path
- fix summary
- targeted test output
- full regression output
- C8 before/after metrics, explicitly separated
- limitations and remaining gaps
- REMOTE_COMMIT

## Acceptance
C9 is complete only when:
- production graph path is executable or explicitly proven unsupported with an actionable reason;
- no silent graph failure remains;
- relevance score survives to the graph consumer path;
- security/lifecycle behavior is unchanged and regression-tested;
- all claims are evidence-classified.

Do not merge another agent's branch. Do not modify Antigravity/Perplexity/LUNA evaluation artifacts.
