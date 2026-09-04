# AI Memory Vault — Planning Influence Experiment V1

Status: EXPERIMENT SPECIFICATION — NOT EXECUTED
Owner: LUNA / AI_MEMORY_VAULT
Round: R001
Evidence level: UNVERIFIED / DESIGN SPECIFICATION

## 1. Objective

Determine whether memory can causally influence agent planning at computation level rather than merely supplying advisory context.

The experiment isolates one channel: Planning Influence.

The experiment also requires a **bounded verification-and-resolution boundary**. Planning influence is not considered complete merely because a branch was selected; the system must verify the resulting trajectory and produce a terminal task result.

## 2. Causal Question

Given the same task, model and search harness, does converting a memory-derived lesson into planner priors/penalties change the search trajectory and improve verified outcome compared with the same lesson delivered only as natural-language context?

## 3. Resolution Semantics

The experiment follows a bounded pipeline:

```text
TASK
  ↓
EXPERIENCE / MEMORY EVIDENCE
  ↓
MODEL / PATTERN
  ↓
APPLICABILITY
  ↓
PLANNING INFLUENCE
  ↓
VERIFICATION LOOP
  ↺ observe → test → challenge → re-evaluate
  ↓
VERIFIED OUTCOME
  ↓
REORGANIZATION
  ↓
TERMINAL TASK RESULT
```

The verification loop is internal and bounded. It exists to establish evidence for the current task.

The terminal result is one of:

```text
FINAL ANSWER
FINAL DECISION
FINAL ABSTENTION
FINAL REQUEST FOR HUMAN CONFIRMATION
```

There is no implicit re-entry into the same task after terminal resolution.

## 4. Critical Distinction

```text
ARM A
memory information -> prompt/context
planner -> uniform/default priors

ARM B
same memory information -> prompt/context
memory influence -> planner priors/penalties
```

The informational content should be matched as closely as practical. The treatment variable is the computational use of memory inside the search harness.

## 5. Environment

Target implementation:

- deterministic or controlled synthetic software-refactoring maze;
- 4 syntactically plausible strategy branches per task;
- at least 3 branches contain executable deadlocks/exceptions or otherwise verifiable failures;
- one or more valid branches;
- real execution oracle for branch success/failure.

Initial proposed scale from adversarial review: 50 tasks.

## 6. Planner

Use a minimal Python search harness. MCTS/PUCT is the first concrete implementation because it gives explicit measurable branch visitation and prior/value channels.

Initial proposed parameters:

```text
depth = 3
rollouts = 8
```

These are experiment parameters, not architecture constants.

## 7. Arms

### Arm A — Advisory Context Control

- same base model;
- same task;
- same planner implementation;
- memory lesson exposed as natural-language context;
- planner uses uniform/default action priors;
- no direct memory-derived prior/penalty enters node selection.

### Arm B — Planning Influence Treatment

- same base model;
- same task;
- same planner implementation;
- same informational lesson available as context;
- memory compiler additionally emits planner prior/penalty data;
- node expansion/selection consumes those values directly.

Example only:

```text
P(strategy_2 | state) = 0.0
P(strategy_3 | state) = 0.8
```

Exact prior values must be fixed before outcome inspection and recorded as experimental configuration.

## 8. Measurements

### Primary

1. Search steps to verified solution.
2. Number of explored nodes.
3. Visits to known-fatal branches.
4. Verified task success rate.
5. Terminal result class: answer / decision / abstention / human confirmation.

### Secondary

6. Search-order divergence between arms.
7. Repeated failure rate.
8. Token/inference cost, where reliably measurable.
9. Wall-clock runtime, reported separately from cognitive efficiency.
10. Verification-loop iterations and verification cost.

### Attribution

Every treatment decision must be traceable through:

```text
memory record
 -> applicability result
 -> MemoryInfluenceState
 -> planner prior/penalty
 -> node-selection trace
 -> action
 -> verification
 -> verified outcome
 -> reorganization
 -> terminal result
```

## 9. Counterfactual Controls

To prevent false attribution:

- shuffle or replace memory entries while keeping prompt length comparable;
- include held-out tasks whose successful branch is not the memorized branch;
- include stale/inapplicable memory cases;
- include contradiction cases where memory should not dominate the current environment;
- keep model, temperature, planner configuration and task ordering fixed or randomized under a declared protocol.

## 10. Success Criteria

The experiment supports Planning Influence only if repeated controlled runs show that Treatment changes search behavior in the expected direction and the change is mechanically attributable to planner-consumed memory priors/penalties.

A better intermediate branch choice alone is insufficient.

A single run where Treatment chooses a better branch is insufficient.

The strongest signature is:

```text
same task + same model + same planner
only computational memory influence differs
        -> systematic search divergence
        -> fewer known-fatal explorations / lower search cost
        -> reproducible verified outcome improvement
        -> traceable attribution
        -> terminal resolution
```

No numerical target is a required acceptance threshold until a baseline pilot establishes variance.

## 11. Falsifiers

Planning Influence is falsified or weakened if:

- the planner ignores the supplied priors/penalties;
- Treatment differs only because of extra context text;
- improvements disappear under matched informational controls;
- priors cause systematic over-pruning of valid branches;
- gains are not reproducible across held-out tasks;
- trace cannot distinguish memory influence from model stochasticity or planner implementation effects;
- a final answer/decision is emitted before required verification completes;
- the same task silently re-enters its own cognitive loop after terminal resolution.

## 12. Deliverables

The executed experiment must produce:

```text
07_EVALUATION/luna/
  PLANNING_INFLUENCE_EXPERIMENT_V1.md        # this specification
  PLANNING_INFLUENCE_RUN_<timestamp>.md     # actual protocol/config
  PLANNING_INFLUENCE_RESULTS_<timestamp>.md # results + evidence level
```

If code is added, it belongs under the implementation/evaluation area selected by the implementation agent and must be committed with reproducible test/runtime output.

## 13. Evidence Discipline

No benchmark result may be marked TEST_VERIFIED or RUNTIME_VERIFIED without actual execution output.

Design assumptions, literature claims and predicted effects remain UNVERIFIED until the repository contains executable evidence.

## 14. Architectural Principle

The five semantic layers are a **bounded task-resolution architecture**:

```text
Experience
    ↓
Model / Pattern
    ↓
Applicability
    ↓
Influence
    ↓
Verification
    ↓
Reorganization
    ↓
FINAL RESPONSE
```

Reorganization modifies memory for future tasks. It does not turn the current task into an infinite memory-update cycle.
