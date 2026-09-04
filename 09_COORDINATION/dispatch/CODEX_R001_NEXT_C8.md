# CODEX — R001 Next Dispatch C8

## Dispatch basis

- Completed task: C1 forensic baseline/retrieval reality
- Prior CODEX branch: `codex/memory-engine-reality-v1`
- Prior evidence: targeted retrieval tests and runtime forensic reports
- Current dispatch controller: `09_COORDINATION/CONTINUOUS_DISPATCH_V1.md`
- Current `main` must be resolved by CODEX immediately before execution.

## Objective

Turn the proven retrieval reality into an executable held-out evaluation and a safe decision point for hybrid retrieval.

## Tasks

1. Start from the actual current `main` SHA; record it.
2. Re-run the existing retrieval regression suite from a clean checkout.
3. Build the held-out lexical-vs-semantic diagnostic suite without changing existing benchmark definitions.
4. Include paraphrase, synonym, antonym, lexical trap, domain-near negative, cross-cluster negative and abstention cases.
5. Exercise the optional local embedding path if available, but do not silently make it authoritative.
6. Report candidate generation, similarity, scoring and final ordering as separate stages.
7. Produce exact stdout/stderr for all executed tests.

## DO NOT CHANGE

- security/lifecycle invariants;
- REVIEW promotion rules;
- existing benchmark definitions;
- canonical memory content merely to improve recall;
- CI claims from another SHA.

## Required evidence

`07_EVALUATION/codex/C8_RETRIEVAL_HOLDBACK.md`

Include baseline SHA, commands, raw outputs, metrics, failures and recommendation.

## Exit condition

Either:

- semantic/hybrid retrieval is demonstrably useful under controlled evaluation; or
- the gap is reproduced and the smallest safe implementation target is identified.
