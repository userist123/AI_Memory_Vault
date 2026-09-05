# R001 C14 — Learning-loop forensics

Evidence level: `CODE_VERIFIED` + `TEST_VERIFIED`. No closed-loop runtime claim is made by this report.

## Baseline

- Branch: `codex/r001-c14-learning-v1`
- Baseline: `061c61ea0dcca24a9e517a9d47b24becd667bbdd`

## Observed data path

`scripts/label_council_outcome.py` appends a JSONL label containing `run_id`, outcome, evidence and label metadata. Its module docstring explicitly states that it is telemetry/evidence only and does not enqueue or promote a memory candidate.

`memory_controller/outcome_tracker.py` writes append-only outcome records to a telemetry ledger, rejects unverified success outcomes, and explicitly forbids canonical vault directories. `memory_controller/memory_trace.py` records IDs present in the final context pack and explicitly documents `OBSERVED != USED`.

`memory_controller/capability_effectiveness.py` can join observed memory IDs with outcome records to produce empirical capability cells. This is analysis of recorded events; it does not feed a changed score or memory content back into retrieval.

`cognitive_core/learning.py` contains a separate `LearningEngine`, but its promotion scan starts from `controller.search(..., "knowledge")` and only considers notes already in `ACTIVE` lifecycle. It can issue an update through `ToolRouter` when relation-count and confidence thresholds are met. The method catches all exceptions from that update and silently continues. The implementation does not read the outcome ledger or observed-memory trace, and does not establish an outcome → evidence → future-retrieval feedback edge.

## Classification

| Link | Result | Evidence |
|---|---|---|
| outcome → append-only evidence | REAL | `CODE_VERIFIED`, `TEST_VERIFIED` |
| context memory IDs → observed trace | REAL | `CODE_VERIFIED`, `TEST_VERIFIED` |
| observed traces + outcomes → effectiveness report | REAL/PARTIAL | `CODE_VERIFIED` |
| outcome → LearningEngine input | NOT IMPLEMENTED | no ledger/trace read in `learning.py` |
| LearningEngine update → future retrieval behavior | UNVERIFIED | no closed-loop runtime test or wiring observed |
| review candidate → canonical promotion without gate | BLOCKED | promotion code requires authorized flow; tests pass |

Overall classification: **PARTIAL / DEAD-END for closed continual learning**.
Telemetry and gated update primitives are real, but the inspected path does not prove that a verified outcome changes what a later retrieval returns.

## Tests actually run

Command:

```text
python -m pytest -q cognitive_core/tests/test_continual_learning.py cognitive_core/tests/test_learning.py cognitive_core/tests/test_label_council_outcome.py memory_controller/tests/test_outcome_tracker.py memory_controller/tests/test_observed_memory_trace.py tests/test_learning_promotion_gate.py
```

Output:

```text
.........................................                                [100%]
41 passed in 0.47s
```

These tests verify telemetry, gating, and local learning behavior. They do not constitute proof of outcome-caused future retrieval improvement.

No source code or canonical memory was changed in this lane.