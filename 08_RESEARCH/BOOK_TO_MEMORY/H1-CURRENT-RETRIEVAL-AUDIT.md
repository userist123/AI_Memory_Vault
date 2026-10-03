# H1 — Current Retrieval Path Audit

## Scope
Audit of the current research/book-to-memory branch against the production retrieval path used by MemoryController.search().
This is an audit artifact only. It does not authorize a production retrieval change and does not promote any research mechanism.

## Repository state audited
- Branch: `research/book-to-memory`
- Base: `main`
- PR: #206
- PR #204 is out of scope.
- The exact PR HEAD is intentionally not duplicated here because this document is a research audit, not a CI status record; GitHub PR metadata is the source of truth for the current revision.
- The audit covers the production retrieval path without changing production retrieval/consolidation implementation.

## Audited production path
MemoryController.search() -> validation/sanitization -> principal lifecycle floor -> QueryClassifier -> RetrievalEngine.retrieve() -> StorageEngine hard gate -> generate_candidates() -> RelevanceScorer -> ranking arm -> optional graph expansion -> optional cognitive stages -> ProgressiveDisclosure -> pagination -> ContextPackBuilder -> candidate_trace/retrieval_trace.

## Finding F1 — Existing v3 runner is not the current production-default ranking baseline
Severity: HIGH for experiment validity; not a production defect.
The current MemoryController.search() defaults active_ranking_arm to RANKING_ARM_FUSED_SCORE when no arm is supplied.
The existing 30_SCRIPTS/evaluation/run_retrieval_benchmark_v3.py explicitly constructs the controller with ranking_arm=RANKING_ARM_BASELINE.
Consequence: a fresh execution of that runner would measure the pre-r025 ranking behavior, not the current production default.
Historical v3 numbers must not be relabeled as the current production baseline. H1 must use the current production default explicitly, or clearly label the baseline arm as historical/pre-r025.
No production code should be changed to resolve this.

## Finding F2 — Candidate generation is query-driven and closes the old head-N reachability gap
Current candidate generation receives the query and the already hard-gated note pool. It computes BM25 and entity rankings over the full gated pool, fuses them with deterministic RRF, and truncates only after ranking.
The current default candidate limit is 200.
The function keeps zero-score entries in a deterministic total ordering, preventing an empty/incomplete candidate set solely because no lexical/entity signal matched.
Audit conclusion: the old claim that storage simply exposes the first N insertion-order notes is not valid for the current path.

## Finding F3 — The actual H1 failure boundary is now separable
The trace exposes hard-policy exclusion, candidate generation, candidate-limit cut, ranking, graph expansion, pagination, and final context-pack budget exclusion.
This is sufficient to classify an H1 miss instead of treating every miss as a generic retrieval failure.

## Finding F4 — Current production ranking still differs materially from candidate-generation ranking
Candidate generation computes BM25/entity RRF, but the production default subsequently uses the fused score as the ranking arm only when the active ranking arm is fused_score.
The legacy baseline arm instead sorts by RelevanceScorer.score(), whose score is (overlap_ratio + confidence) / 2, where confidence is metadata rather than query relevance.
Therefore benchmark arm naming and runner configuration are critical: baseline is not synonymous with current production default.

## Finding F5 — Graph expansion can still be a separate regression point
Graph expansion starts from the ranked seed set. The implementation records seed IDs, expanded IDs, traversed edges, skipped hubs, and final graph contributions.
Strict graph mode can fail closed when a graph-on experiment reports success without actually expanding nodes.
Audit requirement: graph-on and graph-off measurements must record expansion status. A degraded graph-on run must not be interpreted as evidence that graph expansion has no effect.

## Finding F6 — Final context can lose a retrieved candidate after retrieval
After ranking, the system applies progressive disclosure, pagination, and the final context-pack budget.
candidate_trace.final_context_ids is populated only after ContextPackBuilder.build().
Therefore H1 must distinguish candidate recall, ranked/page recall, and final context recall.

## Finding F7 — Pagination is part of the effective retrieval contract
page_size is applied after disclosure and ranking. The next-page token binds query fingerprint, principal, page size, lifecycle/type filters, and disclosure level.
For the current H1 benchmark, first-page behavior must be frozen explicitly. Otherwise pagination may be accidentally measured as part of candidate-generation quality.

## Finding F8 — Experimental HybridRetriever must not be treated as production evidence
retrieval/hybrid_retrieval.py explicitly declares itself experimental and not wired into MemoryController.search().
Its dense embedding path is optional/local and can become unavailable.
It is not valid to use HybridRetriever results as evidence about production retrieval unless an experiment explicitly changes the production path under test.

## Finding F9 — Current trace design is adequate for the first H1 diagnostic
The trace carries IDs/scores rather than note bodies for candidate-generation diagnostics, and final context IDs are recorded after context packing.
This provides a reasonable data-minimized evidence surface for classifying retrieval misses.

## Finding F10 — Local execution is currently blocked by environment networking
A direct repository clone from the execution container failed because DNS/network access to GitHub was unavailable.
No claim of a fresh local benchmark execution is made.
Repository inspection above is based on the GitHub-connected branch contents.

## Required next measurement
1. Freeze the current branch/head and corpus identity.
2. Instantiate the benchmark against the current production default ranking arm (fused_score).
3. Run the current production path with existing lifecycle/security gates unchanged.
4. Capture candidate, ranking, graph, pagination and final-context evidence.
5. Classify every miss by stage.
6. Only then design an associative/context-aware variant if the measured miss pattern justifies it.

## Status
- Production retrieval modified by this audit: NO
- PR #204 modified: NO
- H1 benchmark baseline executed fresh: NO
- Historical v3 numbers treated as current: NO
- Production change justified by this audit alone: NO