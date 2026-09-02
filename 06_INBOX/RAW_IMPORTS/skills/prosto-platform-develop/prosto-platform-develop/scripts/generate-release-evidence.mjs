import { mkdir, writeFile } from 'node:fs/promises';

const releaseVersion =
  process.env.RELEASE_VERSION ?? process.env.npm_package_version ?? '0.0.0-dev';
const commitSha = process.env.GITHUB_SHA ?? 'local';
const runId = process.env.GITHUB_RUN_ID ?? 'local';
const runUrl =
  process.env.GITHUB_SERVER_URL &&
  process.env.GITHUB_REPOSITORY &&
  process.env.GITHUB_RUN_ID
    ? `${process.env.GITHUB_SERVER_URL}/${process.env.GITHUB_REPOSITORY}/actions/runs/${process.env.GITHUB_RUN_ID}`
    : 'local-run';

const now = new Date().toISOString();

const manifest = {
  schemaVersion: '1.0.0',
  releaseVersion,
  generatedAt: now,
  commitSha,
  workflowRun: {
    id: runId,
    url: runUrl,
  },
  checks: [
    {
      id: 'FF-01',
      status: 'pending',
      owner: 'Architecture Owner',
      evidence: 'policy-gates / FF-01 kernel boundary guard',
    },
    {
      id: 'FF-02',
      status: 'pending',
      owner: 'Architecture Owner',
      evidence: 'policy-gates / FF-02 dependency policy guard',
    },
    {
      id: 'FF-03',
      status: 'pending',
      owner: 'Core Runtime Owner',
      evidence: 'quality-gates / FF-03 lifecycle determinism',
    },
    {
      id: 'FF-04',
      status: 'pending',
      owner: 'Core Runtime Owner',
      evidence: 'policy-gates / FF-04 runtime-policy',
    },
    {
      id: 'FF-05',
      status: 'pending',
      owner: 'QA and Core',
      evidence: 'quality-gates / FF-05 contracts gate',
    },
  ],
  exceptions: [],
  notes: [
    'Phase 01 artifact skeleton. Real check statuses are integrated in later phases.',
  ],
};

await mkdir('./.temp/ci', { recursive: true });
await writeFile(
  './.temp/ci/release-evidence.json',
  `${JSON.stringify(manifest, null, 2)}\n`,
  'utf8',
);

console.log('release-evidence generated: .temp/ci/release-evidence.json');
