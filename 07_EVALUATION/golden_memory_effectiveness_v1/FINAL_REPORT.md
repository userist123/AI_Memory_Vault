# NIGHTLY MASTER TASK V1 — final report

## Exact observed values

```text
NIGHTLY_MASTER_TASK_V1

BASE_COMMIT=dab6bcc9303c23f9aa2c53c06bcfc06b69275deb
BENCHMARK_COMMIT=assigned after commit
REMOTE_MAIN=not yet pushed

GOLDEN_TASKS=10
REPEATS_PER_CONDITION=3

CONTROL_EXECUTIONS=30
TREATMENT_EXECUTIONS=30
ORACLE_EXECUTIONS=30

CONTROL_SUCCESS=19
TREATMENT_SUCCESS=19
ORACLE_SUCCESS=18

TREATMENT_WINS=8
CONTROL_WINS=8
BOTH_SUCCESS=11
BOTH_FAILURE=3

MEMORY_HELPFUL=8 observational potential wins
MEMORY_HARMFUL=8 observational potential harms
MEMORY_NEUTRAL=14
MEMORY_UNUSED=0 measured separately
INVALID_RUNS=0

ABSOLUTE_DELTA=0.0
RELATIVE_DELTA=0.0
MCNEMAR_P_VALUE=1.0

ORACLE_GAP_MEAN=-0.03333333333333333
ORACLE_GAP_MEDIAN=0.0

POISONING_CASES=0 executed; 8 specified NOT_RUN
POISONING_BLOCKED=0
POISONING_FAILED=0

HARMFUL_MEMORY_CASES=0 executed; 5 specified NOT_RUN
HARMFUL_MEMORY_BLOCKED=0
HARMFUL_MEMORY_FAILED=0

TEMPORAL_CASES=0 dedicated; existing tests passed
TEMPORAL_PASS=0
TEMPORAL_FAIL=0

PROVENANCE_ATTACKS=0 dedicated; existing tests passed
PROVENANCE_BLOCKED=0
PROVENANCE_FAILED=0

TRACE_TESTS=90
TRACE_INTEGRITY_PASS=90
TRACE_INTEGRITY_FAIL=0

CURRENT_ABLATION_TASKS=0; separate 20-task rerun NOT_RUN
CURRENT_ABLATION_CONTROL_SUCCESS=NOT_RUN
CURRENT_ABLATION_TREATMENT_SUCCESS=NOT_RUN

REGRESSION_TESTS=802 passed, 2 skipped in 18.38s
TARGETED_TESTS=5 passed; safety targeted 71 passed
CI_STATUS=not run for this unpushed benchmark commit

BOOK_ATOMS_USED=0
BOOK_PROVENANCE_LOSSES=0

CANONICAL_MEMORY_MODIFIED=NO
SECRETS_FOUND=NO_MATCHES
DEFENDER_BYPASS_USED=NO

WORKTREE_CLEAN=NO; pre-existing untracked files remain
REMOTE_VERIFIED=NO; push pending

PRIMARY_CONCLUSION=INCONCLUSIVE for memory causality and effectiveness
SECONDARY_CONCLUSION=PROVEN for 90 real local-provider execution traces and their objective verifier outcomes; PARTIALLY_PROVEN for retrieval exposure and security boundaries
KNOWN_GAPS=dedicated poisoning suite, realistic harmful-memory suite, separate current 20-task ablation, dedicated temporal/provenance six-case harnesses, and remote CI
NEXT_BLOCKER=commit and push this benchmark, then verify GitHub Actions and remote main
```

## Interpretation

The primary matrix used `local/qwen2.5-coder:3b` and completed 90 real model
calls. Every control trace had zero retrieval and empty IDs; treatment and
oracle traces recorded memory metadata. Every trace passed the validator. The
8/8 discordant paired outcomes are not causal proof: the trace proves exposure,
not that the model used the memory. The oracle was a bounded page-size-20
retrieval proxy, not a complete document-context oracle.

The initial runner failure and the 90 successful runs are retained in command
history; the first failure exposed the existing harness behavior that real
provider execution does not inject `test_patch`. The benchmark pre-created the
objective verifier in each isolated workspace, so successful runs still prove
model action, real filesystem creation, and real subprocess verification.

Dedicated poisoning and realistic harmful-memory suites were not run and are
not counted as blocked. Temporal and provenance regression tests passed, but
that is not equivalent to the requested new six/five-case end-to-end suites.
The separate current 20-task ablation was also not run.


## 🔗 Legături Sinaptice
- [[07_EVALUATION/README|Evaluation Hub]]
- [[15 Artifacts and Dynamic Evidence Map]]
- [[Knowledge Graph Home]]
