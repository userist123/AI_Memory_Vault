# Golden Memory Effectiveness V1

This artifact records a current-commit experiment, not a claim that memory is
beneficial. Baseline commit: `dab6bcc9303c23f9aa2c53c06bcfc06b69275deb`.

## Primary matrix

The real local provider was `qwen2.5-coder:3b` at `http://localhost:11434`.
The matrix completed 10 tasks × 3 repeats × 3 conditions = 90 executions.
Each run used a fresh workspace and a real pytest verifier. All 90 benchmark
traces pass `trace_validator.py`.

| Condition | Executions | Successes | Rate |
|---|---:|---:|---:|
| CONTROL | 30 | 19 | 63.33% |
| TREATMENT | 30 | 19 | 63.33% |
| FULL_CONTEXT_ORACLE | 30 | 18 | 60.00% |

Treatment/control pairing: 8 treatment wins, 8 control wins, 11 both success,
3 both failure. McNemar exact p-value: `1.0`; absolute delta: `0.0`; relative
delta: `0.0`. This is `STATISTICALLY_UNCERTAIN` evidence, not proof of benefit
or harm. Oracle mean gap (oracle minus treatment): `-0.0333`; median: `0.0`.

The runner recorded `retrieval_count=0` and empty IDs for every control. The
treatment path exposed retrieved IDs and context hashes; at least one treatment
run retrieved multiple memories. The model response, action validation,
workspace diff, and verifier output are retained in `runs_v2/traces/`.

## Interpretation

`MEMORY_HELPFUL=8` and `MEMORY_HARMFUL=8` are observational paired outcome
classifications with memory exposure, not causal proof. The remaining paired
outcomes are neutral/unused according to trace exposure. The experiment does
not prove that retrieved memory was read or causally used by the model. The
oracle condition is a bounded wider retrieval proxy, not a full-document
production baseline.

## Safety suites

The existing regression suite covered security, evidence, temporal, conflict,
and audit tamper behavior (`71 passed` targeted). Dedicated eight-case memory
poisoning, five-case realistic harmful-memory, and current separate 20-task
ablation runs were not executed and are marked `NOT_RUN` in their JSON files.
They must not be counted as blocked or passed.

## Reproducibility and limits

See `REPRODUCIBILITY_MANIFEST.json`, `experiment_manifest.json`, raw
`execution_results.jsonl`, `paired_results.jsonl`, and the trace directory.
The benchmark runner uses the existing `MemoryController`,
`RealAgentExecutionHarness`, `AgentModelExecutor`, `LocalProvider`, and real
subprocess verification. No canonical memory promotion occurs.

Current full regression: `802 passed, 2 skipped in 18.53s`.
