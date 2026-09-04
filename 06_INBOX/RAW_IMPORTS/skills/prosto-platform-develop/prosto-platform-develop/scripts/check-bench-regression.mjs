#!/usr/bin/env node
/**
 * @file check-bench-regression.mjs
 *
 * Bench regression gate.
 *
 * Compares the latest vitest bench JSON output against a committed baseline
 * and exits with a non-zero status when P95 drift exceeds the configured
 * threshold (default 15% = fail, 20% = alert).
 *
 * Usage:
 *   node scripts/check-bench-regression.mjs
 *
 * Environment variables:
 *   BASELINE_FILE          Path to baseline JSON (default: packages/platform-core/bench/baseline.json)
 *   STARTUP_REPORT         Path to startup bench JSON (default: bench-reports/startup.json)
 *   EVENTS_REPORT          Path to events bench JSON (default: bench-reports/events.json)
 *   STARTUP_DRIFT_PERCENT  Override startup fail threshold (default: 15)
 *   EVENTS_DRIFT_PERCENT   Override events fail threshold (default: 15)
 *   EXIT_ON_WARNING        If set to "1", exit non-zero on >20% alert as well
 */

import { existsSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);
const repoRoot = resolve(__dirname, '..');

const BASELINE_FILE = resolve(
  repoRoot,
  process.env.BASELINE_FILE ?? 'packages/platform-core/bench/baseline.json',
);
const STARTUP_REPORT = resolve(
  repoRoot,
  process.env.STARTUP_REPORT ?? 'bench-reports/startup.json',
);
const EVENTS_REPORT = resolve(
  repoRoot,
  process.env.EVENTS_REPORT ?? 'bench-reports/events.json',
);
const STARTUP_DRIFT_PERCENT = Number(process.env.STARTUP_DRIFT_PERCENT ?? 15);
const EVENTS_DRIFT_PERCENT = Number(process.env.EVENTS_DRIFT_PERCENT ?? 15);
const ALERT_PERCENT = 20;
const EXIT_ON_WARNING = process.env.EXIT_ON_WARNING === '1';

function readJson(path) {
  if (!existsSync(path)) {
    throw new Error(`Required file not found: ${path}`);
  }

  return JSON.parse(readFileSync(path, 'utf8'));
}

/**
 * Extract the P95 value, in milliseconds, from a vitest bench JSON.
 *
 * Uses `p99` as a conservative upper-bound proxy for P95 because vitest's
 * tinybench-based JSON output does not include a `p95` field — only
 * `p75`, `p99`, `p995`, and `p999` are available.
 *
 * Vitest bench JSON shape:
 *   { files: [{ groups: [{ benchmarks: [{ name, p75, p99, … }] }] }] }
 *
 * @param {{
 *   files?: Array<{
 *     filepath?: string;
 *     groups?: Array<{
 *       fullName?: string;
 *       benchmarks?: Array<{
 *         name?: string;
 *         p75?: number;
 *         p99?: number;
 *         p995?: number;
 *         p999?: number;
 *         median?: number;
 *         mean?: number;
 *         samples?: number[];
 *       }>
 *     }>
 *   }>
 * }} report
 * @param {string} taskName
 * @returns {number}
 */
function extractP95Ms(report, taskName) {
  if (!report || typeof report !== 'object') {
    throw new Error('Invalid bench report: not an object');
  }

  const benchmarks = (report.files ?? [])
    .flatMap((f) => f.groups ?? [])
    .flatMap((g) => g.benchmarks ?? []);

  const match = benchmarks.find(
    (b) => typeof b?.name === 'string' && b.name === taskName,
  );

  if (!match) {
    throw new Error(`Bench task "${taskName}" not found in report`);
  }

  const p99 = match.p99;

  if (typeof p99 !== 'number' || !Number.isFinite(p99)) {
    throw new Error(`Bench task "${taskName}" has no numeric p99 value`);
  }

  return p99;
}

/**
 * Compute the percent drift from baseline to current, where positive values
 * indicate regression and negative values indicate improvement.
 * @param {number} current
 * @param {number} baseline
 * @return {number}
 */
function computeDriftPercent(current, baseline) {
  if (baseline <= 0) {
    throw new Error(
      `Baseline value must be > 0 to compute drift, got: ${baseline}`,
    );
  }

  return ((current - baseline) / baseline) * 100;
}

function formatDrift(drift) {
  const sign = drift > 0 ? '+' : '';

  return `${sign}${drift.toFixed(2)}%`;
}

/**
 * @param {{name: string, current: number, baseline: number, failPercent: number, alertPercent: number}} input
 */
function evaluateMetric({
  name,
  current,
  baseline,
  failPercent,
  alertPercent,
}) {
  const drift = computeDriftPercent(+current.toFixed(4), +baseline.toFixed(4));
  const status =
    drift > alertPercent ? 'ALERT' : drift > failPercent ? 'FAIL' : 'OK';

  return { name, current, baseline, drift, status };
}

function main() {
  const baseline = readJson(BASELINE_FILE);
  const startupReport = readJson(STARTUP_REPORT);
  const eventsReport = readJson(EVENTS_REPORT);

  const startup = evaluateMetric({
    name: 'startupP95',
    current: extractP95Ms(startupReport, 'startup sequence'),
    baseline: Number(baseline.startupP95Ms),
    failPercent: STARTUP_DRIFT_PERCENT,
    alertPercent: ALERT_PERCENT,
  });

  const events = evaluateMetric({
    name: 'eventDispatchP95',
    current: extractP95Ms(eventsReport, 'event dispatch with handlers'),
    baseline: Number(baseline.eventDispatchP95Us) / 1000,
    failPercent: EVENTS_DRIFT_PERCENT,
    alertPercent: ALERT_PERCENT,
  });

  // Render a markdown table that GitHub Actions will surface in the job log.
  const header = '| Metric | Baseline | Current | Drift | Status |';
  const separator = '|---|---|---|---|---|';
  const rows = [startup, events].map(
    (m) =>
      `| ${m.name} | ${m.baseline.toFixed(4)} | ${m.current.toFixed(4)} | ${formatDrift(m.drift)} | ${m.status} |`,
  );
  const table = [header, separator, ...rows].join('\n');

  console.log(`\n## Bench regression report\n\n${table}\n`);

  const hasFail = [startup, events].some((m) => m.status === 'FAIL');
  const hasAlert = [startup, events].some((m) => m.status === 'ALERT');

  if (hasFail) {
    console.error(
      `\nBench regression gate FAILED: drift exceeded ${STARTUP_DRIFT_PERCENT}% (alert ${ALERT_PERCENT}%).\n`,
    );
    process.exit(1);
  }

  if (hasAlert && EXIT_ON_WARNING) {
    console.error(
      `\nBench regression gate ALERT: drift exceeded ${ALERT_PERCENT}%.\n`,
    );
    process.exit(2);
  }

  console.log('Bench regression gate PASSED.');
}

try {
  main();
} catch (err) {
  console.error(`\ncheck-bench-regression failed: ${err.message}\n`);
  process.exit(1);
}
