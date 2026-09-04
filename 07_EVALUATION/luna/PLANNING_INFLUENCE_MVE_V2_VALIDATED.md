# AI Memory Vault — Planning Influence MVE V2

Status: EXPERIMENT HARNESS SPECIFICATION — NOT EXECUTED
Evidence level: UNVERIFIED / DESIGN SPECIFICATION
Purpose: Minimal isolated causal test for computational planning influence from external memory.

## 1. Causal question

For the same task, model, planner, and execution oracle, does adding a memory-derived soft prior to planner node selection change search behavior and improve verified outcomes compared with matched informational memory delivered only as advisory context?

## 2. Arms

### Arm 1 — Baseline
No memory. Uniform/default planner priors.

### Arm 2 — Passive advisory control
Memory lesson is exposed in context. Planner priors remain uniform/default. Prompt length/content is matched as closely as practical with treatment.

### Arm 3 — Cognitive treatment
The same lesson is exposed in context, and a memory compiler emits a soft, non-clamping prior over branches. The planner consumes that prior directly during selection/expansion.

### Arm 4 — Stale/adversarial memory control
A memory item recommends a branch that is stale or contradicted by the current task state. The applicability layer must downgrade or reject it; the planner must remain capable of exploring a valid branch.

## 3. Synthetic environment

- 30 paired debugging/refactoring scenarios for the first MVE.
- 4 plausible branches per task: 1 optimal, 1 suboptimal, 2 fatal.
- Each branch is executable by a deterministic local oracle.
- At least one branch-specific counterexample is included in the task set.
- Held-out scenarios are not derived from the memorized branch pattern.

## 4. Planner configuration

Initial pilot configuration:

```text
planner = MCTS/PUCT
rollouts = 16
depth = 3
exploration_constant = 1.414
```

These are experiment parameters only and do not become Vault architecture constants.

## 5. Soft prior rule

Treatment priors are empirical/soft, not hard clamps. A memory lesson may increase or decrease branch preference, but it may not make a branch impossible unless the execution oracle independently proves it invalid.

The prior-generation rule and all numeric parameters are frozen before evaluating outcomes. No post-hoc prior tuning is allowed.

## 6. Applicability gate

Memory influence must carry an explicit applicability result:

```text
APPLICABLE
APPLICABLE_WITH_VERIFICATION
NOT_APPLICABLE
INSUFFICIENTLY_KNOWN
```

Only APPLICABLE may directly produce the full treatment prior. The other states must reduce, neutralize, or route the memory influence to verification rather than silently acting as valid advice.

## 7. Attribution trace

Every treated decision must be serializable as:

```text
memory_id
 -> applicability_result
 -> MemoryInfluenceState
 -> branch_prior/penalty
 -> planner node selection
 -> executed branch
 -> oracle outcome
```

The harness must make it possible to distinguish computational memory influence from ordinary prompt/context effects.

## 8. Primary metrics

1. Verified task success rate.
2. Explored nodes before verified solution.
3. Known-fatal branch visits.
4. Search-order divergence versus Arm 2.

Secondary metrics: repeated fatal-branch rate, model inference/token cost where reliable, wall-clock runtime.

## 9. Proposed pilot diagnostic

Before fixing any acceptance threshold, run a small baseline pilot to estimate variance across tasks and arms. The prior proposal of a >=40% node reduction is a candidate hypothesis only, not an acceptance requirement.

## 10. Falsification conditions

Planning Influence is weakened or falsified if:

- Arm 3 does not measurably alter search trajectory;
- Arm 3's advantage disappears under matched informational controls;
- gains are attributable to extra or differently worded context rather than planner-consumed priors;
- stale/adversarial memory systematically over-prunes valid branches;
- treatment improvements do not reproduce on held-out tasks;
- attribution traces cannot connect memory applicability to the planner decision.

## 11. Implementation constraint

The MVE remains isolated under evaluation code. Do not modify the production Vault schema, lifecycle semantics, retrieval contract, or agent execution path merely to improve the benchmark.

## 12. Evidence rule

This file records design only. It must not be cited as runtime evidence. Executed configuration and actual stdout/stderr belong in separate run/result artifacts after execution.
