# Valul 1 — Blocking Repair Directive

## Owner
Antigravity.

## Source
Architecture/Decision report supplied on 2026-09-05. Treat its empirical/code findings as the task input; do not silently reinterpret them.

## Gate 0 — off-by-one defect (BLOCKING)

Find and repair the **real production-code defect** described in the report:

- symptom: off-by-one construction of a series of indicators;
- reported location clue: `pandas/core/common.py:601` in the failing execution environment;
- this is a code defect, not test drift;
- identify the caller/producer in this repository and the exact operation that constructs the series with the wrong length/bound.

Do NOT patch tests to match the defect. Do NOT vendor or modify installed third-party pandas source unless the repository itself contains the offending implementation. The fix must be in the repository-owned production code that causes the invalid-length series construction.

Required deliverables:
1. exact root cause and file/function;
2. minimal production-code fix correcting the boundary/length calculation;
3. a focused regression test asserting the exact expected series length;
4. the existing 11 relevant tests must remain green, plus the new length regression;
5. record the exact test command and observed result in version-controlled evidence.

## Valul 1 parallel work packages

Execute independently where safe, but maintain file ownership isolation:

- WP-1 lifecycle: canonical lifecycle decision and regression evidence.
- WP-2 production retrieval: verify/wire real production call-site; no facade may be considered production unless consumed by a non-test call-site.
- WP-3 corpus: measure current baseline, repair resolvability regressions, preserve exact baseline values as evidence.
- WP-4 pandas/indicator defect: BLOCKING off-by-one repair above.

## Barrier
Do NOT begin Valul 2 (CI guards, branch inventory changes, repo separation) until the complete Valul 1 regression barrier is green, including `20_TESTS/` and the focused off-by-one regression.

## Required coordination rule
Before building any new layer over a component, verify who consumes it in production:

```text
grep -rl "<modul>" --include='*.py' . | grep -v "/tests/\|test_\|benchmarks"
```

If the result is empty, the component is not integrated. Wire it first or move to another work item.

A component named `Production X` that is not called from production is not production.

## Scope constraints
- No changes to `main`.
- No changes to `PROJECT_BRAIN/PROJECT_STATE.md`.
- No force-push/history rewrite.
- No invented test or CI evidence.
- Keep WP changes independently attributable by commit.
