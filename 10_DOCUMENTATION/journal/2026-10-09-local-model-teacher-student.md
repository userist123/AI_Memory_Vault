# Local model teacher–student validation — 2026-10-09

## Scope

This record covers local Ollama validation and the real execution harness. Each
model was run sequentially in an isolated probe; models were stopped between
probes to reduce resource interference. The model received the task first. The
repository test or the real harness verification ran afterward. Failure details
were supplied only for a bounded repair attempt.

## Safety gate

Command:

```text
python 30_SCRIPTS/verification/untrusted_content_guard.py
```

Scope: versioned untrusted roots `06_INBOX`, `.agents/skills`, and
`02_PRODUCT/projects/imported`. Result: `FINDINGS=172`, `BLOCKING=0`, exit code
0. Findings were report-only categories (`role_header`, `false_authorization`,
and `override_instruction`). This is not a claim that every file in the vault
is malware-free.

## B3 live provider smoke test

Command per model:

```text
RUN_LIVE_OLLAMA_TESTS=1 OLLAMA_MODEL_TIERS_CONFIG=04_CONFIG/model_tiers_local.json OLLAMA_MODEL=<model> python -m pytest -q --tb=short 20_TESTS/test_b3_local_provider_live.py
```

Models tested one at a time: `qwen2.5-coder:3b`, `qwen2.5-coder:7b`,
`qwen2.5:7b-instruct`, `mistral:7b-instruct`, `llama3.1:8b`, and
`qwen3:30b-a3b`. Each returned `1 passed`, exit code 0. These are provider
smoke tests, not production quality or full-suite proof.

## Real execution harness

Harness: `03_IMPLEMENTATION/packages/interfaces/real_execution_harness.py`.
It performs model inference, validates structured JSON actions, applies actions
to an isolated workspace, runs real pytest, and persists a JSON trace.

Command per model:

```text
RUN_REAL_PROVIDER_INTEGRATION=1 REAL_PROVIDER_MODE=local REAL_PROVIDER_MODEL=<model> python -m pytest -q --tb=short 20_TESTS/test_model_agent_execution_harness.py::test_real_provider_integration
```

Observed isolated runs: qwen2.5-coder:3b, qwen2.5-coder:7b, qwen2.5:7b-instruct,
llama3.1:8b passed with exit code 0. Mistral showed a stochastic first-run
failure where the generated test omitted the import for `multiply`, producing a
real pytest `NameError` (exit code 1); a later run passed. One repair attempt
overcorrected to `from math import multiply` and produced an `ImportError` (exit
code 2), so repair output is never accepted without another real pytest run.
Qwen3 had an intermittent CUDA host allocation failure (`HTTP 500`, allocation
of a 12.9 GB buffer); isolated fallback runs passed. This is a resource
condition, not evidence of a code defect.

## Coaching rule

When a model fails, provide only the observed failure and acceptance contract,
then rerun the same harness. A step is accepted only when the independent
reviewer sees the required evidence. The sequential workflow in
`03_IMPLEMENTATION/packages/routing/sequential_workflow.py` bounds attempts,
persists checkpoints atomically, and fails closed on unknown status.

## Full repository suite

Command:

```text
python -m pytest -q --tb=short --durations=50
```

The Windows run was interrupted after 28:35 because it remained in the final
zone without progress. It reported `3329 passed, 7 failed, 40 skipped,
9 xfailed`; exit code was interrupted, not a pass. The seven failures were
pre-existing research packet/report freshness mismatches, Windows executable-bit
expectation, and owner-authority CLI return code. They are not represented as
implementation success in PR #252.

## Repair pass

The canonical generators were rerun:

```text
python 30_SCRIPTS/evaluation/b2m_leakage_check.py
python 30_SCRIPTS/evaluation/generate_b06_labelling_packet.py
python 30_SCRIPTS/evaluation/generate_b03_task_packet.py
```

The five artifact freshness/reproducibility tests returned `5 passed`. The
import guard now treats a shebang as executable intent when Windows does not
preserve POSIX mode bits. The owner gate test selects installed Git Bash on
Windows instead of the WSL shim; the focused import/owner suite returned
`57 passed`.
