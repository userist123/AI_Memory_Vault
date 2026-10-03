# H1 Runtime Evidence Contract

## Purpose

This contract defines the runtime evidence required to admit an H1 case into baseline analysis. It complements the structural corpus validator; it does not replace it.

The goal is stage attribution: every miss must be classifiable as policy exclusion, candidate-generation miss, ranking miss, graph regression, pagination/disclosure loss, final context-pack loss, or true absence.

## Identity

Every runtime record must bind:

- `corpus_commit`
- `corpus_hash`
- `benchmark_hash`
- `case_id`
- `run_id`
- `model_id`
- `repetition`
- `variant_id`
- `principal`
- exact frozen query

The record is invalid if corpus or benchmark identity does not match the frozen H1 case set.

## Request contract

Record the effective request state:

- principal
- explicit lifecycle filter
- explicit type filter
- disclosure level
- page size
- context/output budget
- query classification
- classifier-derived filters
- authorization/lifecycle gate result

No benchmark result may silently omit a filter that can change eligibility.

## Candidate evidence

Record, without storing unnecessary note bodies:

- candidate IDs in generated order
- BM25 rank/score where available
- entity rank/score where available
- fused candidate rank/score
- candidate-limit value
- gold candidate rank, or explicit `not_reached`
- distractor ranks where defined

This distinguishes candidate-generation failure from later ranking failure.

## Ranking evidence

Record:

- ranking arm identifier
- ranked IDs
- gold rank after ranking
- tie-breaking information when ties affect position
- ranking configuration/version

A candidate that exists before ranking but disappears from the top-K is a ranking miss, not a candidate-generation miss.

## Graph evidence

For graph-enabled cases record:

- graph requested
- graph actually executed
- seed IDs
- traversed directed edges
- expanded IDs
- skipped hubs/reasons
- graph contribution to final candidate set
- degraded/fail-closed status

A graph-on run that did not actually expand must never be interpreted as a valid graph-on comparison.

## Disclosure, pagination and context evidence

Record:

- page number
- page token/fingerprint binding
- disclosed IDs
- final context IDs
- IDs removed by context budget
- gold presence at each boundary

This separates retrieval success from context-pack omission.

## Performance evidence

Record:

- wall-clock latency
- token usage where available
- errors/exceptions
- timeout status
- deterministic/repeatability status

Performance changes are secondary outcomes; they must not be used to redefine correctness. Failed, timed-out, or degraded executions remain part of the evidence denominator and must be classified rather than silently discarded.

## Selection and denominator integrity

Every structurally valid case in the frozen benchmark packet must produce a runtime record for every planned baseline/variant repetition, unless execution infrastructure fails. A case may not be removed after observing retrieval results because its lexical/entity reachability is inconvenient, trivial, or unfavorable.

Reachability is an observed attribute, not a post-hoc inclusion filter:
- `BASELINE_REACHABLE` — gold is reachable through the declared baseline candidate path;
- `BASELINE_NOT_REACHABLE` — eligible gold is absent from baseline candidate generation;
- `BASELINE_REACHABILITY_UNMEASURABLE` — the trace is insufficient or execution degraded.

For families intended to isolate associative recovery, the preregistration may define a subset such as `BASELINE_NOT_REACHABLE`, but that subset must be declared before results are inspected and its full parent denominator must remain reported. Cases failing the subset criterion are exclusions from that specific subgroup analysis, not deletions from the benchmark.

The benchmark report must publish:
- total frozen cases;
- structurally valid cases;
- executed cases;
- degraded/failed cases;
- cases in each reachability stratum;
- cases excluded from any subgroup and the predeclared exclusion rule.

No subgroup may be defined from the observed variant outcome.

## Derived miss classification

Classification is computed after raw evidence is frozen:

1. `POLICY_EXCLUDED` — gold is rejected by an explicit eligibility/security gate.
2. `CANDIDATE_MISS` — eligible gold is absent from candidate generation.
3. `RANKING_MISS` — gold is generated but falls outside the evaluated ranked boundary.
4. `GRAPH_MISS_OR_REGRESSION` — graph behavior changes reachability or removes a previously reachable result.
5. `PAGINATION_OR_DISCLOSURE_MISS` — gold is ranked but absent from the evaluated page/disclosure boundary.
6. `CONTEXT_PACK_MISS` — gold reaches the disclosed set but is removed by final context budgeting.
7. `RETRIEVED_CORRECTLY` — gold reaches the required final boundary.
8. `UNMEASURABLE` — evidence is incomplete or execution degraded; do not convert this into success or failure.
9. `BASELINE_REACHABILITY_UNMEASURABLE` — baseline reachability cannot be established; retain the case in the denominator and report the missing evidence.

The classification must be deterministic from the frozen trace.

## Baseline/variant rule

Baseline and variant records must have identical:

- corpus identity
- case set
- query wording
- principal
- lifecycle/type gates
- page/disclosure settings
- evaluator rubric

Only the declared experimental mechanism may differ.

## Privacy and evidence minimization

Runtime evidence should store IDs, scores, ranks and trace metadata rather than full memory bodies whenever possible. Full note content is not required for stage attribution and must not be copied into the benchmark artifact unless independently required and authorized.

## Admission gate

A case may enter H1 baseline analysis only when:

1. structural corpus validation passes;
2. corpus identity matches;
3. runtime request state is complete;
4. candidate/ranking/context evidence is complete enough for deterministic attribution;
5. no degraded execution is misclassified as a retrieval result.

No runtime record by itself authorizes a production retrieval change.

## Research status

Research-only. No production behavior is changed by this contract.
