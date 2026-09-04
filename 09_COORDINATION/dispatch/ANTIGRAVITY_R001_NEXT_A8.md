# ANTIGRAVITY R001 — NEXT TASK A8

## Mission
Independently validate the **production graph integration problem and its repair**, without implementing the repair. Build a differential observability package that can falsify whether graph/activation is actually active on real SQLite/File storage paths.

## Start from
Resolve actual `origin/main` before analysis. Record the SHA.

## Inputs
- GAP-012 from A7: production graph construction uses missing `.store` and is silently swallowed.
- GAP-002: relevance score disappears before downstream reranking.
- CODEX C9 may later repair these defects, but do not assume the repair succeeded.

## Required experiment
Create a fixed corpus/query set and compare:
1. Base retrieval only.
2. Graph stage on in-memory storage.
3. Graph stage on SQLite storage.
4. Graph stage on File storage.

For each, capture:
- candidate IDs before graph;
- graph construction status;
- graph candidate IDs added;
- edge/activation contributions;
- relevance score source;
- final rank/order;
- fallback reason, if any;
- elapsed time.

## Falsification targets
- Is graph activation actually invoked in production storage paths?
- Can it add a previously omitted relevant neighbor?
- Does edge weight affect propagation after CODEX's fix?
- Does `relevance_score` survive into the ranking decision?
- Can an exception be silently converted into apparent successful base retrieval?

## Observability rules
- No implementation changes.
- No synthetic `ACTIVE`/`AVAILABLE` statuses.
- If a signal cannot be captured, mark it `UNAVAILABLE`.
- Distinguish `GRAPH_NOT_CALLED`, `GRAPH_FAILED`, `GRAPH_CALLED_NO_EFFECT`, and `GRAPH_CHANGED_RANK`.
- Keep raw machine-readable trace alongside the report.

## Evidence required
`07_EVALUATION/antigravity/A8_PRODUCTION_GRAPH_DIFFERENTIAL.md`
plus a machine-readable artifact under `telemetry/retrieval_traces/`.

The report must include exact commands, stdout/stderr, SHA, corpus definition, query set, rank deltas, and an explicit before/after comparison against A7.

## Acceptance
A8 is complete only when Antigravity can answer, from runtime evidence:
- whether production graph retrieval is live;
- whether it changes candidate reachability/ranking;
- whether the edge-weight repair is observable;
- whether score provenance is preserved;
- whether fallback remains distinguishable from successful graph execution.

Do not modify CODEX, PERPLEXITY, or LUNA evidence artifacts.
