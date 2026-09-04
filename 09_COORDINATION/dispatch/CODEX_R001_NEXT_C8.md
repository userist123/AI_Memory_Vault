# CODEX — R001 Next Dispatch C8

## Dispatch basis

- Completed task: C1/C4 retrieval + graph-activation forensics and defect repair.
- Completed CODEX branch: `codex/r001-retrieval-v2`.
- Completed remote commit: `6a00c6c546ca875b85c160e466f307f3011220a6`.
- C4 local regression: `13 passed in 0.06s`.
- Important: completed branch was based on stale `BASE_MAIN=4764ee4615760a0480abd76ff2633aac25840e03`.
- Current `main` at dispatch creation: `f8a5f99ce00c1abeee90c549a12510dd30aa18ad`.
- Therefore this dispatch MUST NOT assume the completed C4 branch is a descendant of current `main` and MUST NOT merge it blindly.

## Objective

Complete C8: produce a controlled held-out retrieval evaluation on the actual current `main`, then identify the smallest safe next implementation target for semantic/hybrid retrieval.

## Tasks

1. Start from the exact current `main` SHA and record it. If the SHA changes before execution, use the newer SHA and record the change.
2. Re-run the relevant retrieval/graph regression suite from a clean checkout of that exact base.
3. Freeze a held-out diagnostic corpus. Do not modify existing benchmark definitions or historical benchmark data.
4. Evaluate at minimum: exact lexical match, paraphrase, synonym, antonym, lexical trap, domain-near negative, cross-cluster negative and unrelated-abstention cases.
5. Compare the actual default controller path against the optional semantic/embedding path when the latter is available. Keep the optional path explicitly non-authoritative unless the codebase already wires it otherwise.
6. Separate and report these stages independently: candidate generation -> similarity -> relevance scoring -> graph/activation rerank -> final ordering -> abstention.
7. Include a differential run showing whether the repaired edge weights change final ranking/activation when all other inputs are fixed.
8. Capture exact stdout/stderr for every executed command. Do not claim tests, runtime, CI, or live-provider behavior without direct evidence.
9. Report metrics only for the frozen held-out set actually executed. At minimum report counts plus Precision@k / Recall@k / MRR where the dataset supports those measures; otherwise state `UNAVAILABLE` and explain why.
10. End with one of two outcomes: (a) controlled evidence supports a safe semantic/hybrid implementation target, or (b) the retrieval gap remains unverified/reproduced and the smallest safe implementation target is specified.

## DO NOT CHANGE

- security/lifecycle invariants;
- REVIEW promotion rules;
- provenance requirements;
- existing benchmark definitions/history;
- canonical memory merely to improve recall;
- CI claims from another SHA;
- graph logic again unless a new defect is independently reproduced by this task.

## Required evidence

`07_EVALUATION/codex/C8_RETRIEVAL_HOLDBACK.md`

The evidence file MUST include:

- exact base SHA;
- repository/branch HEAD used;
- commands executed;
- raw stdout/stderr or complete captured output;
- corpus definition and query classes;
- stage-by-stage results;
- metrics with denominators;
- failures/skips/environment limitations;
- conclusion and smallest safe implementation target.

## Exit condition

No semantic/hybrid capability may be promoted merely because it exists as an optional component. C8 is complete only when the retrieval reality is measured on a controlled held-out set and the next implementation decision is evidence-backed.
