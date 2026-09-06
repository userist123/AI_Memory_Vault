# Planning Influence — Uncertainty Policy V1

Status: `PROPOSED / PRE-REGISTERED DESIGN`
Evidence level: `UNVERIFIED`
Scope: isolated Planning Influence MVE only
Production Vault changes: `NONE`

## 1. Purpose

The preceding deterministic pilots showed that reducing a memory-derived branch prior can reduce harm, but a scalar attenuation alone does not distinguish four materially different situations:

1. the memory is applicable and well-supported;
2. the memory appears applicable but requires verification;
3. the memory is insufficiently known;
4. the memory is not applicable.

This policy fixes those distinctions before the next pilot. It is not an implementation claim.

## 2. State model

Every treated decision must carry:

```text
applicability
 evidence_strength
 contradiction_state
 verification_cost
 planner_influence
 execution_outcome
```

### Applicability states

```text
APPLICABLE
APPLICABLE_WITH_VERIFICATION
INSUFFICIENTLY_KNOWN
NOT_APPLICABLE
```

### Evidence strength

Continuous value in `[0.0, 1.0]`, supplied independently of the task oracle.

### Contradiction state

```text
NONE
POSSIBLE_CONTRADICTION
CONFIRMED_CONTRADICTION
```

## 3. Pre-registered influence policy

Planner influence is computed from applicability and evidence strength, with contradiction acting as a safety veto.

```text
NOT_APPLICABLE or CONFIRMED_CONTRADICTION
    -> neutral planner prior

INSUFFICIENTLY_KNOWN
    -> at most weak directional influence
    -> no execution gating

APPLICABLE_WITH_VERIFICATION
    -> weak directional influence
    -> emits verification requirement
    -> valid branches remain explorable

APPLICABLE
    -> evidence-weighted directional influence
    -> no hard branch clamp
```

For the isolated deterministic harness, the pre-registered directional prior rule is:

```text
base_prior = 0.25
influence_budget = 0.40
winner_prior = base_prior + influence_budget * applicability_strength * evidence_strength
remaining_mass = 1 - winner_prior
loser_prior = remaining_mass / 3
```

Fixed applicability strengths:

```text
APPLICABLE                  = 1.00
APPLICABLE_WITH_VERIFICATION = 0.35
INSUFFICIENTLY_KNOWN        = 0.15
NOT_APPLICABLE              = 0.00
```

The numeric constants above are frozen for the next deterministic run. They must not be altered after observing results.

## 4. Verification routing

`APPLICABLE_WITH_VERIFICATION` is not treated as equivalent to `APPLICABLE`.

The harness must record a verification requirement separately from planner selection. The next experimental revision should model verification as an explicit action/cost where possible rather than silently pretending that a weak prior is sufficient evidence.

The planner must remain capable of selecting a branch different from the memory recommendation.

## 5. Safety properties

The following are mandatory:

- no hard clamping of valid actions;
- no oracle access when generating applicability or evidence strength;
- no post-hoc calibration;
- contradiction cannot increase planner influence;
- neutral states produce uniform priors;
- every treated trace records the complete memory-to-planner attribution chain;
- stale/adversarial memory must remain a falsification condition, not a hidden success case.

## 6. Primary comparison

The next deterministic pilot will compare:

```text
Arm 1 — baseline / uniform
Arm 2 — advisory / uniform planner
Arm 3 — cognitive treatment / frozen uncertainty policy
Arm 4 — stale or contradicted memory / safety control
```

Primary metrics remain:

1. verified success rate;
2. nodes before verified solution;
3. known-fatal visits;
4. search-order divergence versus Arm 2.

Additional uncertainty metrics:

- verification requests;
- verification cost;
- influence strength actually applied;
- contradiction-triggered neutralization;
- false-positive memory influence;
- false-negative memory influence.

## 7. Falsification conditions

The policy is rejected or revised if any of the following is observed:

- uncertain memory still causes systematic fatal exploration without measurable benefit;
- verification requests do not reduce harmful memory influence;
- contradiction states still influence planning positively;
- the treatment advantage exists only when the task oracle leaks into applicability/evidence generation;
- treatment remains worse than matched advisory control across held-out scenarios without a compensating verified-outcome benefit.

## 8. Promotion gate

No model-backed paired MVE is authorized solely because deterministic tests pass.

The deterministic harness must first demonstrate that the uncertainty policy is mechanically correct and either:

- provides a reproducible planning benefit under matched controls, or
- produces a clear falsification result that motivates a new pre-registered policy.

This document itself is design evidence only and must not be cited as runtime proof.


## 🔗 Legături Sinaptice
- [[07_EVALUATION/README|Evaluation Hub]]
- [[15 Artifacts and Dynamic Evidence Map]]
- [[Knowledge Graph Home]]
