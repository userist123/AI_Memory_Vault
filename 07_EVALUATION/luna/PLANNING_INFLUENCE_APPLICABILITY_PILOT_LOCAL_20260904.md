# Planning Influence Applicability-Aware Pilot — 2026-09-04

> [!WARNING]
> **ERRATUM (2026-09-16 — Superseded by Planning Influence V3)**:
> Rezultatul raportat aici (`baseline: 30 noduri · 0 fatale` vs `treatment: 54 noduri · 12 fatale`) a fost invalidat metodologic: brațul de bază a rezolvat 30/30 de scenarii în primul pas exclusiv datorită scurgerii de oracol prin ordonarea ramurilor (`optimal = order[0]`) și departajării pe primul index (`-branches.index(candidate)`). Când ordinea este amestecată legitim, baza consumă 87.3 noduri și 29.8 fatale.
> Consultați raportul științific complet și autoritativ: [`PLANNING_INFLUENCE_RESULTS_V3.md`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/07_EVALUATION/luna/PLANNING_INFLUENCE_RESULTS_V3.md).

## Evidence

- Evidence level: `HISTORICAL_RECORD_SUPERSEDED`
- CI status: `UNVERIFIED` for the latest applicability-aware commit; GitHub Actions MVE run remained queued when checked.
- Production Vault code modified: `NO`
- Experiment harness only: `YES`
- Oracle leakage guard: compiler reads only independently frozen `memory_recommended`; task `optimal` is not used to generate priors.

## Frozen applicability input

The treatment arm now consumes an independently frozen applicability sequence containing all four contract states:

- `APPLICABLE`: 8 scenarios
- `APPLICABLE_WITH_VERIFICATION`: 8 scenarios
- `INSUFFICIENTLY_KNOWN`: 7 scenarios
- `NOT_APPLICABLE`: 7 scenarios

No applicability label is generated from the task oracle.

## Local result

```text
arm1_baseline:   success=30/30 nodes=30 fatal=0
arm2_advisory:   success=30/30 nodes=30 fatal=0
arm3_treatment:  success=30/30 nodes=54 fatal=12
arm4_stale:      success=30/30 nodes=30 fatal=0

treatment_vs_advisory_node_reduction=-0.8000
```

Post-hoc treatment quality strata:

```text
match:    count=7  success=7  nodes=7  fatal=0
mismatch: count=23 success=23 nodes=47 fatal=12
```

## Interpretation

The applicability gate reduces the adverse influence observed in the preceding pilot (`125 nodes / 15 fatal`) but does not yet produce a net planning-efficiency gain. The treatment arm still expands search relative to advisory control and records 12 known-fatal visits.

The result therefore remains a **negative/falsification-oriented calibration result**. It does not justify promotion to model-backed cognitive-planning evidence.

No post-hoc parameter tuning was performed after seeing these outcomes.

## Next decision

Do not freeze the current numerical attenuation as a successful calibration. Before the model-backed MVE, evaluate a principled uncertainty policy in the isolated harness, with explicit separation between:

1. memory applicability,
2. evidence strength,
3. verification cost,
4. planner influence,
5. execution outcome.

The next change must be pre-registered before execution and must not use the task oracle to set applicability, priors, or verification state.


## 🔗 Legături Sinaptice
- [[07_EVALUATION/README|Evaluation Hub]]
- [[15 Artifacts and Dynamic Evidence Map]]
- [[Knowledge Graph Home]]
