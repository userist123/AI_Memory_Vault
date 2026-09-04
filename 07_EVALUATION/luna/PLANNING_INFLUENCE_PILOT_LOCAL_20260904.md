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

## Interpretation

The deterministic harness mechanics and regression tests executed successfully. The experiment does **not** support a positive planning-efficiency claim in its current configuration.

The treatment changed planner behaviour, but the frozen memory recommendation matched the optimal branch in only 7/30 scenarios. Consequently, the treatment explored substantially more nodes and fatal branches than the advisory control.

This is a useful negative result: it separates **mechanical planning influence** from **memory quality/calibration**. The current prior is capable of steering search, but steering with an unreliable memory can degrade search efficiency.

## Decision

- Do not promote this pilot to evidence that cognitive memory improves planning.
- Do not tune parameters after observing outcomes merely to obtain a positive result.
- Improve instrumentation and memory-quality/calibration controls before the model-backed causal experiment.
- GitHub Actions remains the authoritative CI source; its runs were still queued during this session and therefore provide no CI evidence yet.
