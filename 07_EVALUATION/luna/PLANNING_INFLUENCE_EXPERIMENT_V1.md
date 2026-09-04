# AI Memory Vault — Planning Influence Experiment V1

Status: EXPERIMENT SPECIFICATION — NOT EXECUTED
Owner: LUNA / AI_MEMORY_VAULT
Round: R001
Evidence level: UNVERIFIED / DESIGN SPECIFICATION

## 1. Objective

Determine whether memory can causally influence agent planning at computation level rather than merely supplying advisory context.

The experiment isolates one channel: Planning Influence.

## 2. Causal Question

Given the same task, model and search harness, does converting a memory-derived lesson into planner priors/penalties change the search trajectory and improve outcome compared with the same lesson delivered only as natural-language context?

## 3. Critical Distinction

```text
ARM A
memory information -> prompt/context
planner -> uniform/default priors

ARM B
same memory information -> prompt/context
memory influence -> planner priors/penalties
```

The informational content should be matched as closely as practical. The treatment variable is the computational use of memory inside the search harness.

## 4. Environment

Target implementation:

- deterministic or controlled synthetic software-refactoring maze;
- 4 syntactically plausible strategy branches per task;
- at least 3 branches contain executable deadlocks/exceptions or otherwise verifiable failures;
- one or more valid branches;
- real execution oracle for branch success/failure.

Initial proposed scale from adversarial review: 50 tasks.

## 5. Planner

Use a minimal Python search harness. MCTS/PUCT is the first concrete implementation because it gives explicit measurable branch visitation and prior/value channels.

Initial proposed parameters:

```text
depth = 3
rollouts = 8
```

These are experiment parameters, not architecture constants.

## 6. Arms

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

## 7. Measurements

### Primary

1. Search steps to verified solution.
2. Number of explored nodes.
3. Visits to known-fatal branches.
4. Verified task success rate.

### Secondary

5. Search-order divergence between arms.
6. Repeated failure rate.
7. Token/inference cost, where reliably measurable.
8. Wall-clock runtime, reported separately from cognitive efficiency.

### Attribution

Every treatment decision must be traceable through:

```text
memory record
 -> applicability result
 -> MemoryInfluenceState
 -> planner prior/penalty
 -> node-selection trace
 -> action
 -> outcome
```

## 8. Counterfactual Controls

To prevent false attribution:

- shuffle or replace memory entries while keeping prompt length comparable;
- include held-out tasks whose successful branch is not the memorized branch;
- include stale/inapplicable memory cases;
- include contradiction cases where memory should not dominate the current environment;
- keep model, temperature, planner configuration and task ordering fixed or randomized under a declared protocol.

## 9. Success Criteria

The experiment supports Planning Influence only if repeated controlled runs show that Treatment changes search behavior in the expected direction and the change is mechanically attributable to planner-consumed memory priors/penalties.

A better outcome alone is insufficient.

A single run where Treatment chooses a better branch is insufficient.

The strongest signature is:

```text
same task + same model + same planner
only computational memory influence differs
        -> systematic search divergence
        -> fewer known-fatal explorations / lower search cost
        -> reproducible outcome improvement
        -> traceable attribution
```

No numerical target is a required acceptance threshold until a baseline pilot establishes variance.

## 10. Falsifiers

Planning Influence is falsified or weakened if:

- the planner ignores the supplied priors/penalties;
- Treatment differs only because of extra context text;
- improvements disappear under matched informational controls;
- priors cause systematic over-pruning of valid branches;
- gains are not reproducible across held-out tasks;
- trace cannot distinguish memory influence from model stochasticity or planner implementation effects.

## 11. Deliverables

The executed experiment must produce:

```text
07_EVALUATION/luna/
  PLANNING_INFLUENCE_EXPERIMENT_V1.md        # this specification
  PLANNING_INFLUENCE_RUN_<timestamp>.md     # actual protocol/config
  PLANNING_INFLUENCE_RESULTS_<timestamp>.md # results + evidence level
```

If code is added, it belongs under the implementation/evaluation area selected by the implementation agent and must be committed with reproducible test/runtime output.

## 12. Evidence Discipline

No benchmark result may be marked TEST_VERIFIED or RUNTIME_VERIFIED without actual execution output.

Design assumptions, literature claims and predicted effects remain UNVERIFIED until the repository contains executable evidence.
