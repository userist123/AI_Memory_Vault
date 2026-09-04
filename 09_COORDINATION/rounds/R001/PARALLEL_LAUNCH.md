---
type: coordination
category: parallel-round
status: active
version: 1.0.1
round_id: "R001"
---

# AI Memory Vault — Parallel Round R001

## Baseline

The launch document was created while coordination commits were still being added. The authoritative R001 baseline is therefore the **current `main` SHA resolved immediately before agent startup**, not a hard-coded stale value.

At the latest manifest verification, `main` resolves to:

`2f4c8360acb00f40ea1b2bc7dd132873d8dea4e9`

This value must be re-resolved by every agent before starting. If `main` has moved, that newly observed SHA becomes the actual round baseline and the mismatch must be recorded.

## Objective

Convert the current architecture into reproducible evidence about real cognitive behavior while preserving security and lifecycle invariants.

The round is parallel by default. Unrelated tasks do not wait for each other.

## Lane assignments

### CODEX — R001-C1..C7

C1. Establish exact retrieval runtime path and candidate-generation behavior.

C2. Build held-out lexical-vs-semantic retrieval evaluation without changing benchmark definitions.

C3. Implement end-to-end memory-content injection tests at the retrieval-consuming boundary, preserving fail-closed security.

C4. Build a controlled graph/activation A/B harness with fixed candidate sets.

C5. Test temporal validity, supersession and contradiction behavior.

C6. Trace outcome telemetry into any downstream consumer; classify loop closure honestly.

C7. Implement only defects reproduced by C1-C6; run deterministic regression and CI.

CODEX may modify implementation/test surfaces and `07_EVALUATION/codex/` only.

### ANTIGRAVITY — R001-A1..A6

A1. Produce a retrieval execution trace that exposes candidate generation, scoring and ranking.

A2. Produce score-distribution views separating semantic/relevance/confidence/authority signals.

A3. Produce graph/activation comparison instrumentation for fixed candidate sets.

A4. Produce lifecycle/provenance visualization showing REVIEW versus ACTIVE and supersession.

A5. Extend memory-use/outcome observability without asserting causality from presence alone.

A6. Maintain an architecture gap register tied to concrete source locations and observed behavior.

ANTIGRAVITY must not repair core retrieval/security/lifecycle semantics without an explicitly assigned implementation task.

### PERPLEXITY — R001-P1..P7

P1. Research memory poisoning and instruction/data separation.

P2. Research hybrid lexical + embedding retrieval suitable for deterministic evaluation.

P3. Research hard-negative and held-out retrieval evaluation.

P4. Research calibration, abstention and selective prediction.

P5. Research graph/associative-memory mechanisms and meaningful evaluation methods.

P6. Research outcome-driven learning with explicit human gating and anti-reward-hacking controls.

P7. Research temporal/provenance-aware retrieval and contradiction handling.

Every recommendation must state evidence, limitations and a testable acceptance criterion.

### LUNA / GPT-5.6 — R001-L1..L9

L1. Independently verify baseline and delta from the previous barrier.

L2. Maintain held-out retrieval challenges not supplied to CODEX.

L3. Attack the retrieval-to-agent memory-content trust boundary with novel payloads.

L4. Independently test graph/activation claims using controlled comparisons.

L5. Audit temporal, supersession and provenance semantics.

L6. Audit score meaning, calibration and abstention claims.

L7. Audit outcome learning and distinguish telemetry from actual loop closure.

L8. Audit causal claims using MEMORY PRESENT / RETRIEVED / FINAL CONTEXT / USED / CAUSED OUTCOME.

L9. Reconcile all lane outputs at the barrier and issue ACCEPT / REWORK / REJECT decisions.

LUNA may modify only `07_EVALUATION/luna/` in this round.

## Isolation

Each lane has a disjoint evidence namespace:

- CODEX → `07_EVALUATION/codex/`
- ANTIGRAVITY → `07_EVALUATION/antigravity/`
- PERPLEXITY → `07_EVALUATION/perplexity/`
- LUNA → `07_EVALUATION/luna/`

No agent edits another lane's artifacts.

## Parallel start rule

All four lanes may begin immediately after resolving the current baseline.

Research does not wait for implementation.
Observability does not wait for research.
Independent audit does not wait for either.
Implementation does not wait for unrelated audit work.

A dependency blocks only the specific subtask that truly requires it.

## Evidence barrier

At completion each lane produces a packet containing:

- round ID;
- actual baseline SHA;
- task IDs;
- commands or methods;
- raw evidence references;
- evidence levels;
- failures/blockers;
- changed files;
- commit SHA where applicable;
- CI evidence where applicable;
- recommended next action.

The barrier is written to:

`09_COORDINATION/rounds/R001/BARRIER.md`

No automatic merge occurs at the barrier.

## Integration policy

Only approved implementation changes are integrated.

Integration requires:

1. ownership collision check;
2. source diff review;
3. regression tests;
4. security invariant check;
5. exact-SHA CI verification;
6. post-integration Luna verification.

## Non-negotiable exclusions

Do not:

- weaken security controls;
- promote REVIEW to ACTIVE for benchmark gains;
- rewrite history to hide regressions;
- change benchmark definitions silently;
- claim CI from another SHA;
- claim causal effectiveness from memory presence;
- treat a research recommendation as implemented capability.

## Success condition

R001 succeeds only if the evidence materially reduces uncertainty about:

1. actual retrieval mechanism;
2. actual graph/activation effect;
3. memory-content instruction authority risk;
4. score/calibration semantics;
5. temporal correctness;
6. learning-loop closure;
7. causal memory effectiveness.

A partial result with reproducible blockers is a valid round outcome.
