# Performance Regression Budgets

## Overview

This document describes the performance regression budgets for the prosto-platform runtime. These budgets define acceptable performance thresholds and are enforced in CI to prevent performance regressions.

## Thresholds

| Metric | Baseline | Warning | Critical |
|--------|----------|---------|----------|
| Startup P95 | 100ms | +15% drift | +20% drift |
| Event Dispatch P95 | 500μs | +15% drift | +20% drift |

## Benchmark Configuration

### Default Configuration

```typescript
// packages/platform-core/bench/regression-budget.config.ts
export const DEFAULT_REGRESSION_BUDGET = {
  warmupIterations: 100,
  measuredIterations: 1000,
  time: 5000,
};
```

### Benchmark Files

| File | Description |
|------|-------------|
| `bench/startup.bench.ts` | Measures startup sequence performance |
| `bench/events.bench.ts` | Measures event dispatch performance |

## Running Benchmarks

### Local Execution

```bash
# Run specific benchmark (writes JSON report to bench-reports/)
npm run bench:startup
npm run bench:events

# Compare latest reports against the committed baseline
npm run bench:regression
```

### CI Execution

Benchmarks run on every pull request to detect performance regressions.
The `bench-regression` job in
[`.github/workflows/quality-gates.yml`](../../.github/workflows/quality-gates.yml)
executes the full regression gate:

```yaml
# .github/workflows/quality-gates.yml (excerpt)
bench-regression:
  name: FF-06 perf-regression
  runs-on: ubuntu-latest
  steps:
    - run: npm ci
    - run: npm run build
    - run: npm run bench:startup
    - run: npm run bench:events
    - run: npm run bench:regression
      env:
        BASELINE_FILE: packages/platform-core/bench/baseline.json
        STARTUP_REPORT: bench-reports/startup.json
        EVENTS_REPORT: bench-reports/events.json
        STARTUP_DRIFT_PERCENT: 15
        EVENTS_DRIFT_PERCENT: 15
```

Failure thresholds:

| Drift | Status | Effect |
|-------|--------|--------|
| ≤ 15% | OK | Job passes |
| > 15% and ≤ 20% | Alert | Job log warns; `EXIT_ON_WARNING=1` would fail |
| > 20% | Fail | Job fails (default behaviour) |

## Baseline Management

### Updating Baselines

Baselines should be updated when:

1. Significant architectural changes are made
2. New optimization techniques are implemented
3. Hardware/CI environment changes

### Baseline Evidence

Baseline evidence is stored in the configuration file and should include:

- Measured P95 values
- Date of measurement
- Environment description
- Number of iterations

## Drift Calculation

```typescript
driftPercent = ((currentValue - baselineValue) / baselineValue) * 100
```

### Interpretation

| Drift | Status | Action |
|-------|--------|--------|
| < 0% | Improvement | No action needed |
| 0-15% | Within budget | Monitor |
| 15-20% | Warning | Investigate |
| > 20% | Critical | Block merge |

## Flakiness Mitigation

To reduce benchmark flakiness:

1. **Warmup iterations**: 100 iterations before measurement
2. **Median-of-runs**: Use median instead of mean
3. **Dedicated runner profile**: Isolated CI environment
4. **Minimum run time**: 5 seconds per benchmark

## Risk-to-Control Mapping

The full risk-to-control mapping for the performance controls described in
this document lives in [`risk-control-matrix.md`](risk-control-matrix.md). It
links each risk (R-PERF-01..R-PERF-03) to its implementing benchmark, gate
script, and CI job.

| Risk ID | Control | Benchmark | Gate Script | CI Gate |
|---------|---------|-----------|-------------|---------|
| R-PERF-01 | Startup regression gate | [`packages/platform-core/bench/startup.bench.ts`](../../packages/platform-core/bench/startup.bench.ts:1) | [`scripts/check-bench-regression.mjs`](../../scripts/check-bench-regression.mjs:1) | `bench-regression` ([`.github/workflows/quality-gates.yml`](../../.github/workflows/quality-gates.yml)) |
| R-PERF-02 | Event-dispatch regression gate | [`packages/platform-core/bench/events.bench.ts`](../../packages/platform-core/bench/events.bench.ts:1) | [`scripts/check-bench-regression.mjs`](../../scripts/check-bench-regression.mjs:1) | `bench-regression` |
| R-PERF-03 | Baseline calibration guard | n/a (uses both bench files) | [`scripts/calibrate-bench-baseline.mjs`](../../scripts/calibrate-bench-baseline.mjs:1) | PR review of `packages/platform-core/bench/baseline.json` |

## Related Documents

- [Risk-to-Control Matrix](risk-control-matrix.md)
- [Module Loading Policy](../security/module-loading-policy.md)
- [Security Risk-to-Control Matrix](../security/risk-control-matrix.md)
- [ADR-0007: Observability](../../.context/02-architecture-design/adr/ADR-0007-observability.md)
