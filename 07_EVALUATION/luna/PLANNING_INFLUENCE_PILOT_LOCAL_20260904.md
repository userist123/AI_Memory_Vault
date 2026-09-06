# Planning Influence MVE V2 — Local Deterministic Pilot

Date: 2026-09-04
Evidence level: RUNTIME_VERIFIED (local execution)
Source code: exact `planning_influence_mve.py` content retrieved from canonical `main`
Validation constraint: the execution environment could not clone GitHub directly; the file was reconstructed locally from the exact GitHub `main` content before execution. This is therefore local runtime evidence, not GitHub Actions CI evidence.

## Command evidence

```text
6 passed in 0.06s
LOCAL_EXECUTION_EVIDENCE=TRUE
scenario_count=30
arm1_baseline: success=30/30 nodes=30 fatal=0
arm2_advisory: success=30/30 nodes=30 fatal=0
arm3_treatment: success=30/30 nodes=125 fatal=15
arm4_stale: success=30/30 nodes=30 fatal=0
treatment_vs_advisory_node_reduction=-3.1667
recommendation_match_optimal_count=7
```

## Memory-quality diagnostic

The post-hoc quality analysis is deliberately separate from planner execution:

```text
QUALITY_SUMMARY_PASS=TRUE
match_count= 7
match_nodes= 7
match_fatal= 0
mismatch_count= 23
mismatch_nodes= 118
mismatch_fatal= 15
```

This confirms that the treatment's degradation is concentrated in the 23 scenarios where the frozen memory recommendation disagreed with the oracle-optimal branch. The diagnostic does not feed back into the planner and does not alter priors.

## Interpretation

The deterministic harness mechanics and regression tests executed successfully. The experiment does **not** support a positive planning-efficiency claim in its current configuration.

The treatment changed planner behaviour, but memory quality is the limiting factor in this pilot: only 7/30 recommendations matched the optimal branch. Correct recommendations required 7 nodes with no fatal visits in the observed run; mismatched recommendations required 118 nodes with 15 fatal visits.

This negative result is retained as evidence rather than tuned away. It separates **mechanical planning influence** from **memory quality/calibration**. The current prior mechanism is capable of steering search, but steering with unreliable memory can materially degrade search efficiency.

## Decision

- Do not promote this pilot to evidence that cognitive memory improves planning.
- Do not tune parameters after observing outcomes merely to obtain a positive result.
- Preserve the negative result as a calibration/quality warning.
- Add uncertainty-aware prior attenuation and explicit applicability/verification states before the model-backed causal experiment.
- GitHub Actions remains the authoritative CI source; its runs were still queued during this session and therefore provide no CI evidence yet.


## 🔗 Legături Sinaptice
- [[07_EVALUATION/README|Evaluation Hub]]
- [[15 Artifacts and Dynamic Evidence Map]]
- [[Knowledge Graph Home]]
