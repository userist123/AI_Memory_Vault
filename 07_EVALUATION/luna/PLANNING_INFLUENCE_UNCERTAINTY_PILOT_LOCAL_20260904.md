# Planning Influence — Uncertainty Policy Pilot

Date: 2026-09-04
Evidence level: `RUNTIME_VERIFIED` (local reconstructed exact-source execution)
CI evidence: `UNVERIFIED`
Production Vault changes: `NONE`

## Frozen policy

The deterministic harness uses the pre-registered uncertainty policy from `PLANNING_INFLUENCE_UNCERTAINTY_POLICY_V1.md`.

Inputs are independent of the task oracle:

- applicability sequence: 8 `APPLICABLE`, 8 `APPLICABLE_WITH_VERIFICATION`, 7 `INSUFFICIENTLY_KNOWN`, 7 `NOT_APPLICABLE`;
- evidence strengths: frozen tuple in the harness;
- contradiction states: frozen tuple in the harness;
- confirmed contradiction and `NOT_APPLICABLE` neutralize planner influence;
- `APPLICABLE_WITH_VERIFICATION` emits an explicit verification requirement;
- no hard action clamp;
- no post-hoc parameter tuning.

## Runtime result

```text
baseline:   success=30/30 nodes=30 fatal=0
advisory:   success=30/30 nodes=30 fatal=0
treatment:  success=30/30 nodes=54 fatal=12 verification=8
stale:      success=30/30 nodes=30 fatal=0

treatment_vs_advisory_node_reduction=-0.8000
```

## Safety diagnostics

```text
APPLICABLE=8
APPLICABLE_WITH_VERIFICATION=8
INSUFFICIENTLY_KNOWN=7
NOT_APPLICABLE=7
confirmed_contradiction_neutralized=7
verification_requests=8
```

## Memory-quality strata

```text
match:    count=7 nodes=7 fatal=0 success=7
mismatch: count=23 nodes=47 fatal=12 success=23
```

## Decision

The frozen uncertainty policy is **mechanically implemented and safety-routed, but not yet planning-efficient**. Treatment still expands the search relative to matched advisory control and produces known-fatal visits. This is a negative/falsification-oriented result, not an acceptance result.

The result is nevertheless useful: the policy now distinguishes applicability, evidence strength, verification requirement, contradiction, influence, and outcome in one traceable state. The next experiment should model verification as an explicit planner action/cost rather than only metadata attached to the selected branch.

Do not advance to the model-backed paired MVE solely from this result.
