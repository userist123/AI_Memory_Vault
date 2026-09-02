#!/usr/bin/env node
/**
 * @file calibrate-bench-baseline.mjs
 *
 * Re-baselines the performance regression budget by running startup and
 * event-dispatch benchmarks and writing the measured P95 values to
 * `packages/platform-core/bench/baseline.json`.
 *
 * Intended to be run periodically (and on the canonical CI runner) to
 * refresh the committed baseline. The output is deterministic per-run but
 * intentionally not version-pinned — the resulting PR should be reviewed
 * before merge.
 *
 * Usage:
 *   node scripts/calibrate-bench-baseline.mjs
 *
 * Environment variables:
 *   BASELINE_FILE          Target path for the new baseline (default: packages/platform-core/bench/baseline.json)
 *   ENVIRONMENT            Label recorded in the baseline (default: ci-ubuntu-latest)
 */

import { execSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);
const repoRoot = resolve(__dirname, '..');

const BASELINE_FILE = resolve(
  repoRoot,
  process.env.BASELINE_FILE ?? 'packages/platform-core/bench/baseline.json',
);
const REPORTS_DIR = resolve(repoRoot, 'bench-reports');
const STARTUP_REPORT = resolve(REPORTS_DIR, 'startup.json');
const EVENTS_REPORT = resolve(REPORTS_DIR, 'events.json');
const ENVIRONMENT = process.env.ENVIRONMENT ?? 'ci-ubuntu-latest';

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

function main() {
  if (!existsSync(REPORTS_DIR)) {
    mkdirSync(REPORTS_DIR, { recursive: true });
  }

  console.log('Running startup benchmark...');
  execSync('npm run bench:startup', { cwd: repoRoot, stdio: 'inherit' });

  console.log('Running event-dispatch benchmark...');
  execSync('npm run bench:events', { cwd: repoRoot, stdio: 'inherit' });

  const startupReport = readJson(STARTUP_REPORT);
  const eventsReport = readJson(EVENTS_REPORT);

  const startupP95Ms = extractP95Ms(startupReport, 'startup sequence');
  const eventDispatchP95Us =
    extractP95Ms(eventsReport, 'event dispatch with handlers') * 1000;

  const previous = existsSync(BASELINE_FILE) ? readJson(BASELINE_FILE) : {};
  const next = {
    ...previous,
    startupP95Ms: Number(startupP95Ms.toFixed(4)),
    eventDispatchP95Us: Number(eventDispatchP95Us.toFixed(4)),
    establishedAt: new Date().toISOString(),
    environment: ENVIRONMENT,
    notes:
      'Calibrated by scripts/calibrate-bench-baseline.mjs. ' +
      'Review the diff before merging and update the regression budget only after the change is intentional.',
  };

  writeFileSync(BASELINE_FILE, `${JSON.stringify(next, null, 2)}\n`, 'utf8');

  console.log(
    `\nBaseline updated at ${BASELINE_FILE}\n` +
      `  startupP95Ms:       ${next.startupP95Ms}\n` +
      `  eventDispatchP95Us: ${next.eventDispatchP95Us}\n` +
      `  environment:        ${next.environment}\n` +
      `  establishedAt:      ${next.establishedAt}\n`,
  );
}

try {
  main();
} catch (err) {
  console.error(`\ncalibrate-bench-baseline failed: ${err.message}\n`);
  process.exit(1);
}
