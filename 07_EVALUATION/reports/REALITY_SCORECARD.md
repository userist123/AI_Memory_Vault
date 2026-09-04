# TOTAL_SYSTEM_REALITY_TEST_01

This is an execution report, not a claim that every documented subsystem is
complete. The run started from local `main` at `2ce5ffab6`; `origin/main` was
`ae6cded98`, so the local consolidation commit is one commit ahead and has not
been pushed. Existing untracked user files were preserved.

## Reality matrix

| Component | Claimed | Runtime | Evidence | Status |
|---|---|---|---|---|
| Memory controller | secure persistent memory API | `MemoryController.search()` returned 5 real IDs in live run | live harness trace; 1,201 regression tests | PROVEN_REAL |
| Retrieval | protected retrieval | real search executed under `Principal.AI_AGENT` | live trace, secure recall tests | PROVEN_REAL |
| Context construction | bounded context and hash | SHA-256 context hash persisted | live trace; harness tests | PROVEN_REAL |
| Relevance scoring | semantic/multisignal ranking | default path is lexical/weighted; vector/entity/graph paths are not default | `07_EVALUATION/retrieval_diagnostic_report.json` | REAL_BUT_PARTIAL |
| Progressive disclosure | budgeted context | exercised by regression tests | context budget/progressive-disclosure tests | PROVEN_REAL |
| Conflict detection | conflict and relation handling | candidate relation classifier and semantic conflict tests execute | consolidation output; conflict tests | PROVEN_REAL |
| Security | P0-P15 trust boundaries | adversarial tests pass and reject unauthorized operations | security/adversarial test suites | PROVEN_REAL |
| Provenance | immutable source lineage | provenance and evidence tamper tests pass | evidence verifier and provenance tests | PROVEN_REAL |
| Knowledge | durable notes and candidates | real vault/SQLite content was retrieved | live trace; storage tests | PROVEN_REAL |
| Skills | catalog and routing | physical catalog exists; runtime effectiveness is not established | skill runtime tests and baseline gap | REAL_BUT_PARTIAL |
| Agents | agent roles | role validation and real harness path execute | harness tests; live model trace | REAL_BUT_PARTIAL |
| Orchestration | multi-agent execution | council bridge has a live Ollama smoke test; full causal graph is absent | live Ollama test; baseline gaps | REAL_BUT_PARTIAL |
| Planner | planning/complexity | deterministic planning tests pass | planner/complexity tests | PROVEN_REAL |
| Council | provider-backed council | one live local-provider council test passed | `test_b3_local_provider_live.py` | REAL_BUT_PARTIAL |
| Model provider | local real model | `qwen2.5-coder:3b` generated valid JSON action | live stdout and trace | PROVEN_REAL |
| Real execution harness | model→action→workspace→verification | successful live run created `answer.py` and passed pytest | persistent live trace | PROVEN_REAL |
| Tool authorization | fail-closed role/action boundary | unauthorized role/action tests pass | tool router and adversarial tests | PROVEN_REAL |
| Workspace execution | actual filesystem/subprocess work | file was created and real pytest returned exit code 0 | live trace | PROVEN_REAL |
| Telemetry | execution metadata | provider, latency, memory IDs, actions, verification captured | live trace | PROVEN_REAL |
| Trace persistence | durable JSON/JSONL traces | trace file and JSONL persisted | `reports/total_system_reality_test_01/traces/` | PROVEN_REAL |
| Evaluation | retrieval/full-context efficacy | existing artifacts contain claims; this run did not reproduce the full benchmark | evaluation artifacts only | UNVERIFIED |
| Ablation | memory causal benefit | committed 20-task local-Ollama result was not rerun in this test | `07_EVALUATION/memory_ablation_2026-09.json` | UNVERIFIED |
| Book ingestion | PDF→extraction | prior GitHub Actions extraction is successful | run `33806399882`, 6 books/4,055 pages | PROVEN_REAL |
| Knowledge consolidation | candidate→atoms with provenance | 54 candidates mapped, 31 atoms generated, no canonical writes | consolidated derivation and 4 tests | PROVEN_REAL |
| Learning | outcome-driven update | gates and tests exist; causal online loop not proven | learning tests and baseline gaps | REAL_BUT_PARTIAL |
| Reflection | self-critique | reflection notes/tests exist; external verification separation not end-to-end proven | reflection tests | REAL_BUT_PARTIAL |
| Routing | query/agent routing | deterministic routing paths execute; effectiveness not established | routing tests | REAL_BUT_PARTIAL |
| CI | repository workflows | prior relevant workflows are green; this local commit has no remote CI run | GitHub Actions API runs | PROVEN_REAL |

Counts: `COMPONENTS_TOTAL=28`, `PROVEN_REAL=18`, `REAL_BUT_PARTIAL=8`,
`UNVERIFIED=2`, `BROKEN=0`, `PLACEHOLDER=0`.

## Direct runtime observations

The real-provider harness run used provider `local`, model
`qwen2.5-coder:3b`, and returned `response_status=success` in about 501 ms.
The model emitted a JSON `write_file` action. The action was validated for the
`synthesizer` role, created `answer.py` in a temporary workspace, and the real
command `python -m pytest test_answer.py -q` returned `1 passed`, exit code 0.
The persistent trace contains `retrieved_memory_ids` (5), a context hash, the
action, workspace diff, and verification result.

The first live attempt exposed a real harness limitation: on the real-provider
branch, `test_patch` is not injected, so verification failed with exit code 4.
This was reproduced, not hidden; the successful rerun pre-created the test in
the workspace. The harness therefore proves the runtime path, but not full
agent-generated test-file orchestration.

## Adversarial and comparison accounting

The regression corpus exercised eight attack categories: unauthorized
promotion/attestation, privileged provenance forgery, provenance mutation,
lifecycle escalation, tool authorization, cache poisoning, audit tampering,
and invalid evidence/schema. All observed security assertions passed:
`ATTACKS=8`, `ATTACKS_BLOCKED=8`, `ATTACKS_FAILED=0`. A dedicated malicious
memory prompt-injection run was not performed and remains a gap.

The existing ablation artifact defines 20 paired tasks, but it was not rerun in
this execution. The only new memory run retrieved five real memories but had no
no-memory control, so causal helpfulness is unmeasured:
`MEMORY_TASKS=1`, `MEMORY_HELPFUL=0`, `MEMORY_HARMFUL=0`, `MEMORY_NEUTRAL=1`.
No paired full-context run was performed: `RETRIEVAL_TASKS=0`,
`RETRIEVAL_WINS=0`, `FULL_CONTEXT_WINS=0`, `TIES=0`.

The ten-task golden suite and three-repeat protocol were not created or run in
this pass: `GOLDEN_TASKS=0`, `GOLDEN_TASK_SUCCESS=0`,
`GOLDEN_TASK_FAILURE=0`. This is an explicit acceptance gap, not a fabricated
success.

Book consolidation was independently regenerated and tested: 54 candidates,
12 clusters, 10 cross-source synthesis atoms, 21 single-source atoms, 0
provenance losses. The full relation matrix retained all 1,431 pairs.

## Required final report

```text
BASE_COMMIT=2ce5ffab620d6d275ee1c7ec4645c27d8d352e8b
FINAL_COMMIT=assigned after this report commit

COMPONENTS_TOTAL=28
PROVEN_REAL=18
REAL_BUT_PARTIAL=8
UNVERIFIED=2
BROKEN=0
PLACEHOLDER=0

GOLDEN_TASKS=0
GOLDEN_TASK_SUCCESS=0
GOLDEN_TASK_FAILURE=0

ATTACKS=8
ATTACKS_BLOCKED=8
ATTACKS_FAILED=0

MEMORY_TASKS=1
MEMORY_HELPFUL=0
MEMORY_HARMFUL=0
MEMORY_NEUTRAL=1

RETRIEVAL_TASKS=0
RETRIEVAL_WINS=0
FULL_CONTEXT_WINS=0
TIES=0

BOOKS=6
BOOK_CANDIDATES=54
BOOK_SYNTHESIS_ATOMS=10
BOOK_PROVENANCE_LOSSES=0

TRACE_TESTS=7
TRACE_INTEGRITY_PASS=7

SECURITY_INVARIANTS_TESTED=15
SECURITY_INVARIANTS_PASS=15

TARGETED_TESTS=54 passed
FULL_TESTS=1201 passed, 73 warnings
CI_STATUS=prior relevant runs SUCCESS; local final commit NOT RUN remotely

REALITY_GAPS=dedicated golden-task/repeatability protocol; paired full-context retrieval run; dedicated malicious-memory prompt-injection run; end-to-end external verification of reflection; causal memory attribution; default semantic/entity/graph retrieval
NEXT_BLOCKERS=run and persist the missing golden suite, repeat each nondeterministic task three times, rerun the 20-task ablation on the current commit, and push the local commit to obtain CI for it
```

## What remains if documentation is deleted?

The executed core path—secure retrieval, context hashing, local model
inference, action validation, real workspace mutation, subprocess verification,
and persistent trace—is demonstrably real. The effectiveness claims for memory,
retrieval, learning, and reflection are not all proven. A defensible overall
percentage is therefore not reported: the component count above measures
implementation evidence, while runtime reality and causal effectiveness are
separate dimensions.
