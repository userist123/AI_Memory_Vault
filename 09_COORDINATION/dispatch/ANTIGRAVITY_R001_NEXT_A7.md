# ANTIGRAVITY — R001 Next Dispatch A7

## Dispatch basis

- Completed task: A1-A6 observability/architecture inspection
- Prior branch: `antigravity/observability-v1`
- Prior evidence: retrieval trace, score distribution, lifecycle census, knowledge map, associative audit and observability gaps
- Current dispatch controller: `09_COORDINATION/CONTINUOUS_DISPATCH_V1.md`
- Current `main` must be resolved independently immediately before execution.

## Objective

Convert the observed retrieval reality into a reproducible differential trace that can distinguish base controller ranking from associative reranking and expose score causality.

## Tasks

1. Resolve and record the actual current `main` SHA.
2. Build or extend a developer-only trace for one fixed corpus and fixed query family.
3. Capture BASE controller output before associative processing.
4. Capture BASE + activation/graph output with identical candidates.
5. Report exact rank deltas, score deltas and inclusion/exclusion reasons.
6. Explicitly expose unavailable signals as `UNAVAILABLE`.
7. Investigate the edge-weight overwrite as an observability finding, not as an implementation fix.
8. Produce reproducible traces and command outputs where execution is available.

## DO NOT CHANGE

- security decisions;
- lifecycle enforcement;
- canonical memory values;
- CODEX implementation;
- LUNA or PERPLEXITY evidence.

## Required evidence

`07_EVALUATION/antigravity/A7_ASSOCIATIVE_DIFFERENTIAL_TRACE.md`

Include baseline SHA, method, fixed corpus/query set, raw outputs, rank changes, missing signals and next recommendation.

## Exit condition

Either:

- graph/activation materially changes ranking and relevance under controlled comparison; or
- the mechanism is shown to be structurally present but behaviorally ineffective/unproven.
