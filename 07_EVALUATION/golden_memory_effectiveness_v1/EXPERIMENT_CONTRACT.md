# Golden Memory Effectiveness V1 — experimental contract

Version: `1.0.0`  |  Source baseline: recorded in `REPRODUCIBILITY_MANIFEST.json`

## Conditions

`CONTROL` disables `MemoryController.search()` and must persist
`retrieval_count=0`, `retrieved_memory_ids=[]`, and an empty memory query.
`TREATMENT` calls the secure controller search with the task query.
`FULL_CONTEXT_ORACLE` uses the same controller and task query with the largest
bounded page size supported by this experiment; it is a diagnostic upper-context
condition, not a production retrieval design.

## Outcome rules

A run succeeds only when the real verification subprocess exits zero and the
harness reports success. A failed model call, rejected action, missing trace, or
failed verifier is a failure/invalid run according to the validator. Memory is
`HELPFUL` only when treatment succeeds while control fails, retrieved context is
present in the model input, and the action/verification path differs. It is
`HARMFUL` when treatment fails while control succeeds with the same task state
and treatment exposed memory. Equal outcomes are `NEUTRAL` only when memory was
exposed and `UNUSED` when no memory was exposed. These are observational
classifications; they do not prove causal influence without a randomized paired
design and appropriate uncertainty analysis.

## Non-evidence

Retrieval count, memory presence, a successful model response, equal outputs,
or a higher aggregate success rate alone are not causal evidence. Self-report
or model commentary is not external verification. A historical report is not a
current run.

## Required metadata and isolation

Every task has a machine verifier. Each condition/repeat uses a fresh workspace,
the same task definition, explicit `task_seed`, `run_seed`, `condition_order`,
and the current git commit. Raw traces are retained. No result is promoted to
canonical memory.

## Additional suites

Poisoning, harmful-memory, temporal, and provenance cases are safety probes.
They measure exposure and blocking separately. A retrieved poison that is
rejected by authorization is different from a poison never retrieved.


## 🔗 Legături Sinaptice
- [[07_EVALUATION/README|Evaluation Hub]]
- [[15 Artifacts and Dynamic Evidence Map]]
- [[Knowledge Graph Home]]
