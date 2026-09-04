# Performance Risk-to-Control Matrix

## Overview

This document maps the high and critical performance risks from
[`.context/02-architecture-design/06-risk-management.md`](../../.context/02-architecture-design/06-risk-management.md)
to the runtime benchmarks, automated tests, and CI gates that detect and
prevent regressions. It is the authoritative evidence store for
performance-control coverage in prosto-platform and is updated whenever a
benchmark, threshold, or gate changes.

## Risk-to-Control Mapping

| Risk ID | Risk | Control | Code | Tests | CI Gate |
|---------|------|---------|------|-------|---------|
| R-PERF-01 | Startup time regresses due to new dependencies, eager initialization, or module-loading refactors | Startup P95 benchmark + 15%/20% regression gate | [`packages/platform-core/bench/startup.bench.ts`](../../packages/platform-core/bench/startup.bench.ts:1), [`scripts/check-bench-regression.mjs`](../../scripts/check-bench-regression.mjs:1) | [`packages/platform-core/tests/benchmarks/regression-budget.test.ts`](../../packages/platform-core/tests/benchmarks/regression-budget.test.ts:1) | `bench-regression` (FF-06 perf-regression) job in [`.github/workflows/quality-gates.yml`](../../.github/workflows/quality-gates.yml) |
| R-PERF-02 | Event-dispatch latency regresses due to listener accumulation, sync hot paths, or handler explosion | Event-dispatch P95 benchmark + 15%/20% regression gate | [`packages/platform-core/bench/events.bench.ts`](../../packages/platform-core/bench/events.bench.ts:1), [`scripts/check-bench-regression.mjs`](../../scripts/check-bench-regression.mjs:1) | [`packages/platform-core/tests/benchmarks/regression-budget.test.ts`](../../packages/platform-core/tests/benchmarks/regression-budget.test.ts:1) | `bench-regression` job |
| R-PERF-03 | Baseline drift caused by stale `baseline.json` (drift that is not a code regression) | Calibratable baseline with audit trail (`notes` and `establishedAt` in baseline.json) | [`packages/platform-core/bench/baseline.json`](../../packages/platform-core/bench/baseline.json:1), [`scripts/calibrate-bench-baseline.mjs`](../../scripts/calibrate-bench-baseline.mjs:1) | unit tests on `checkRegressionThreshold` | `bench-regression` job + dedicated PR review for any baseline bump |

## Thresholds and Reproducibility

| Metric | Baseline | Warn (CI alert) | Fail (CI block) |
|--------|----------|-----------------|-----------------|
| Startup P95 | `packages/platform-core/bench/baseline.json` `startupP95Ms` | +15% drift | +20% drift |
| Event Dispatch P95 | `packages/platform-core/bench/baseline.json` `eventDispatchP95Us` | +15% drift | +20% drift |

Warmup iterations (100) and measured iterations (1000) are pinned in
[`packages/platform-core/bench/regression-budget.config.ts`](../../packages/platform-core/bench/regression-budget.config.ts:1)
to reduce flakiness.

## CI Gate Reference

The `bench-regression` job in
[`.github/workflows/quality-gates.yml`](../../.github/workflows/quality-gates.yml):

1. Sets up Node.js 22 with the npm cache.
2. Runs `npm ci` and `npm run build` to produce a clean benchmark target.
3. Runs `npm run bench:startup` → `bench-reports/startup.json`.
4. Runs `npm run bench:events` → `bench-reports/events.json`.
5. Runs `npm run bench:regression`, which reads
   `packages/platform-core/bench/baseline.json` and the two bench reports
   and fails the job when drift exceeds 15% (alert at 20% via logs only).
6. Uploads `bench-reports/` as a workflow artifact for forensic analysis.

Required env vars (set by the workflow):
`BASELINE_FILE`, `STARTUP_REPORT`, `EVENTS_REPORT`, `STARTUP_DRIFT_PERCENT=15`,
`EVENTS_DRIFT_PERCENT=15`.

## Local Reproduction

```bash
# Run the benchmarks and write JSON reports
npm run bench:startup
npm run bench:events

# Compare against the committed baseline
npm run bench:regression
```

To intentionally bump the baseline after a justified optimization,
run on the canonical CI runner:

```bash
npm run bench:calibrate
```

Open a dedicated PR with the resulting diff in
`packages/platform-core/bench/baseline.json` and link to the relevant
risk-to-control evidence in this document.

## Updating This Matrix

When a benchmark, threshold, or gate changes:

1. Update the table row and the "Thresholds and Reproducibility" section.
2. Update the `bench-regression` job in
   [`.github/workflows/quality-gates.yml`](../../.github/workflows/quality-gates.yml)
   if the threshold or script changes.
3. Reference this matrix from the change PR description so reviewers can
   confirm coverage deltas.
4. If a new performance risk is introduced, add the risk ID in the
   upstream [`06-risk-management.md`](../../.context/02-architecture-design/06-risk-management.md)
   document first, then mirror it here.

## Related Documents

- [`regression-budgets.md`](regression-budgets.md) — thresholds and run profiles
- [`../security/risk-control-matrix.md`](../security/risk-control-matrix.md) — security risks
- [`../../.context/02-architecture-design/06-risk-management.md`](../../.context/02-architecture-design/06-risk-management.md) — risk register source of truth
- [`AGENTS.md`](../../AGENTS.md) — common commands and CI gate list
