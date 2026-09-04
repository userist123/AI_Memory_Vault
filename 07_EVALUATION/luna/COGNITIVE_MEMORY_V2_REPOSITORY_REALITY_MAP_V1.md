# AI Memory Vault — Cognitive Memory V2 Repository Reality Map V1

Status: DESIGN / REPOSITORY MAPPING — NOT IMPLEMENTATION
Author: LUNA (repository inspection)
Project: AI_MEMORY_VAULT
Round: R001
Evidence level: CODE_VERIFIED where explicitly marked; otherwise UNVERIFIED DESIGN MAPPING

## 1. Purpose

This document maps the Cognitive Memory Target Model V2 onto the current repository as observed on `main` and identifies what is already present, what is partial, and what is missing for the first Planning Influence experiment.

This is intentionally a mapping, not a refactor proposal. No core Vault schema change is required by this document.

## 2. Ground Truth Boundary

Repository:

`userist123/AI_Memory_Vault_CODEX_READY`

Observed main tree SHA at inspection start:

`da05ab7ae1c0174ea7f01f6165a8a41e734ba315`

Important: the project continuity CURRENT document on main contains an older `current_main_sha`; the Git tree itself is the stronger source of truth for this mapping.

## 3. V2 → Existing Repository

### 3.1 Experience / Evidence / Provenance

Existing capabilities:

- persistent note storage and lifecycle handling in `memory_controller/controller.py`;
- provenance validation and audit logging are already part of the controller pipeline;
- consolidation and reconsolidation exist in `cognitive_core/consolidation.py`;
- cognitive recall can include REVIEW notes while explicitly tagging them `_cognitive_unverified`.

Assessment: FOUNDATION EXISTS.

Evidence: CODE_VERIFIED for the cited source files.

Missing for V2:

- no canonical, machine-oriented Experience → Pattern → Applicability → Influence contract;
- no explicit transition record connecting a prior episode to planner priors.

### 3.2 Retrieval / Candidate Generation

Current `memory_controller/context/retrieval.py` calls `storage.query()` and truncates to a bounded candidate set. It does not itself perform semantic or structural candidate generation.

Current `memory_controller/context/relevance_scoring.py` uses token overlap plus confidence averaging.

Assessment: CANDIDATE GENERATION / RANKING EXISTS, BUT IS NOT V2 APPLICABILITY-AWARE.

Implication:

Retrieval can supply candidates, but current search cannot be treated as proof that a memory transfers causally to the present task.

Evidence: CODE_VERIFIED.

### 3.3 Associative Recall

`cognitive_core/recall.py` already combines semantic similarity, working-memory relevance, confidence/authority, activation and temporal factors; it preserves REVIEW as read-only cognitive input and supports abstention.

Assessment: PARTIAL PREDECESSOR OF `MemoryInfluenceState`.

What can be reused conceptually:

- activation;
- temporal validity;
- confidence/authority separation;
- lifecycle-aware recall;
- review-gated uncertainty.

What is missing:

- explicit applicability decision independent from relevance score;
- explicit influence payload;
- planner-facing prior representation.

Evidence: CODE_VERIFIED.

### 3.4 Context Compilation / Token Economy

`memory_controller/context/pack_builder.py` enforces byte and token budgets, applies degradation and records observed memory traces.

Assessment: STRONG EXISTING FOUNDATION for progressive disclosure and compact transport.

Missing:

- V2-specific compilation from full memory record to a compact influence card;
- operator-preservation checks for applicability-critical negations and conditions.

Evidence: CODE_VERIFIED.

### 3.5 Graph / Associative Structure

`cognitive_core/ranked_search.py` builds a multi-graph and applies spreading activation as an opt-in ranking layer.

However, on current `main`, `cognitive_core/spreading_activation.py` still contains a direct overwrite of the weighted propagation calculation with hop-decay-only propagation. Therefore graph edge weights are not currently behaviorally preserved in the final `propagated` value.

Assessment: GRAPH SUBSTRATE EXISTS; WEIGHTED PROPAGATION BUG REMAINS ON MAIN.

Evidence: CODE_VERIFIED.

This is important because a future Planning Influence experiment must not attribute changes to graph-derived priors while the graph weighting path is known to be defective.

### 3.6 Consolidation / Reorganization

`cognitive_core/consolidation.py` supports challenge → RECONSOLIDATING and either resolution back to ACTIVE or downgrade to REVIEW, preserving previous content/version information and conflicting evidence.

It also consolidates REVIEW lesson nodes into a new REVIEW knowledge node and archives source lessons.

Assessment: REORGANIZATION FOUNDATION EXISTS.

Missing for V2:

- explicit causal attribution gates before outcome-derived updates;
- prediction-error representation;
- conditional boundary splitting as a first-class memory transformation;
- empirical planner-value update path.

Evidence: CODE_VERIFIED.

## 4. Four Influence Channels — Current Readiness

| Channel | Current substrate | Readiness |
|---|---|---|
| Representation | Context packs, recall, semantic/activation signals | PARTIAL |
| Planning | No production planner-facing MemoryInfluenceState or search-prior interface identified | MISSING |
| Epistemic | Abstention, temporal factors, lifecycle gating, provenance | PARTIAL |
| Execution | Existing controller security/lifecycle boundaries, but no V2 memory-derived execution gateway identified | PARTIAL |

The project should not claim any channel is runtime-demonstrated solely because its conceptual ingredients exist.

## 5. Planning Influence — Exact Gap

The repository currently has retrieval/ranking components but no verified path of the form:

```text
historical episode
  -> consolidated pattern
  -> applicability evaluation
  -> planner prior
  -> search-node selection
  -> outcome
```

This is the principal missing path required by V2.

The first implementation should therefore live at the evaluation/runtime boundary, not by prematurely rewriting the canonical storage schema.

## 6. Minimal V2 Runtime Surface for Planning MVE

The smallest useful conceptual interfaces are:

```text
EpisodeRecord
PatternRecord
ApplicabilityResult
MemoryInfluenceState
PlanningPrior
PlannerTrace
OutcomeRecord
```

Minimal information flow:

```text
Acquisition Trial
    ↓
EpisodeRecord
    ↓
Pattern / empirical value
    ↓
ApplicabilityResult
    ↓
MemoryInfluenceState
    ↓
softmax-derived planner priors
    ↓
MCTS / PUCT node selection
    ↓
PlannerTrace
    ↓
OutcomeRecord
```

This can be isolated under `07_EVALUATION/luna/experiments/planning_mve/` without changing the production controller contract.

## 7. Acquisition vs Oracle Boundary

The Planning MVE must not inject hand-authored `P=0.0 / P=0.8` values as memory evidence.

The repository must instead persist or synthesize an antecedent acquisition record, derive empirical values from that record, and then transform those values into priors.

The prior is therefore a derived artifact, not a source fact.

Recommended provenance chain:

```text
Task 0 execution trace
  -> observed branch outcome
  -> evidence record
  -> consolidated transition/pattern
  -> applicability decision
  -> empirical Q
  -> softmax prior
```

## 8. Applicability — Reuse Candidates

Existing recall has temporal and version-related checks, but V2 needs a separate transfer decision.

Minimum decision vocabulary:

```text
APPLICABLE
APPLICABLE_WITH_VERIFICATION
CONTRAINDICATED
INSUFFICIENT_EVIDENCE
```

The current code does not yet expose this as a canonical contract.

For the MVE, this can be implemented locally to the experiment rather than changing production memory semantics.

## 9. Epistemic Influence — Current Readiness

Current pieces already support parts of the concept:

- abstention threshold in RecallEngine;
- temporal validity factors;
- contradiction/reconsolidation lifecycle;
- REVIEW tagging;
- provenance and audit logging.

But these are not yet a single epistemic decision mechanism that can choose `ACT / VERIFY / EXPLORE / ABSTAIN` from structured applicability and evidence state.

Assessment: DESIGN-READY, RUNTIME PATH MISSING.

## 10. Execution Influence — Current Readiness

The repository already has authorization, security checks, lifecycle restrictions and controlled tool routing around memory operations.

What is not yet demonstrated is a separate memory-derived execution gateway in which an externally derived invariant is evaluated immediately before an unrelated high-risk tool dispatch.

Assessment: SECURITY SUBSTRATE EXISTS; V2 EXECUTION-INFLUENCE EXPERIMENT NOT YET PRESENT.

## 11. Token Economy — Current Reality

The current code already contains meaningful token/byte budget enforcement and observed-memory telemetry in `ContextPackBuilder`.

This should be reused rather than replaced.

The key V2 extension is not a new budget system. It is a compilation stage that chooses the smallest influence representation that preserves:

- conditions;
- negation/operators;
- target state;
- applicability boundary;
- evidence reference.

Full evidence remains outside the agent working context.

## 12. Critical Repository Findings Before MVE

### Blocking / precondition

1. There is no verified production planning-influence path yet.
2. The main branch currently contains the spreading-activation weight-overwrite bug; graph-derived planning evidence must not depend on the broken behavior.
3. Current retrieval relevance is still heavily lexical/token-overlap driven.
4. Current consolidation is not a causal attribution engine.

### Reusable assets

1. lifecycle and provenance controls;
2. associative recall signals;
3. temporal/version handling;
4. consolidation/reconsolidation;
5. context/token budgeting;
6. audit and memory trace mechanisms.

### Do not change yet

1. canonical storage schema;
2. core lifecycle semantics;
3. production search contract;
4. global security behavior.

The first MVE should sit beside these systems and prove the new causal mechanism independently.

## 13. Smallest Implementation Surface for Next Week

When implementation resumes, the minimal work package should be:

```text
07_EVALUATION/luna/experiments/planning_mve/
    acquisition.py
    consolidation.py
    applicability.py
    memory_prior.py
    mcts.py
    protocol.py
    runner.py
    results.py
```

This is a planning boundary, not a claim that these exact files must be created.

The production Vault should be adapted only after the MVE establishes measurable causal influence.

## 14. Evidence Standard

Current conclusions:

- repository capabilities described above: CODE_VERIFIED where tied directly to source files;
- V2 planning influence: UNVERIFIED until runtime experiment;
- causality from outcome to memory reorganization: UNVERIFIED;
- graph-conditioned planning using current main graph weights: BLOCKED by the known weighting defect;
- token-preserving cognitive compilation: DESIGN-READY but UNVERIFIED.

## 15. Exact Next Action

Before implementation:

```text
1. Finalize MVE protocol and anti-confound controls.
2. Keep acquisition and prior derivation explicit.
3. Keep memory and planner interfaces separate.
4. Do not touch core storage schema.
5. When implementation resumes, execute a pilot before scaling.
```

NEXT: use this map as the implementation handoff and experimental boundary for the first Planning Influence MVE.
