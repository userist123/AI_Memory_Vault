import type { IPlatformConfig } from '@/runtime/index.js';
import { createHash } from 'node:crypto';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { RuntimeErrorCodes } from '@/common/index.js';
import {
  createManifest,
  createRuntime,
  TestModule,
} from '@/tests/fixtures/index.js';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';

describe('runtime loader sources', () => {
  let tempDir: string;

  beforeAll(() => {
    tempDir = mkdtempSync(join(tmpdir(), 'prosto-config-test-'));
  });

  afterAll(() => {
    rmSync(tempDir, { recursive: true, force: true });
  });

  it('keeps backward compatibility for memory module refs', async () => {
    const baseConfig = {
      platform: { startupPolicy: 'strict' },
      modules: { artifactCache: { enabled: false } },
    } as IPlatformConfig;

    writeFileSync(
      join(tempDir, 'app_settings.json'),
      JSON.stringify(baseConfig),
    );

    const manifestA = createManifest({ id: 'module-a' });
    const moduleA = new TestModule();

    const runtime = await createRuntime({
      modules: [{ manifest: manifestA, module: moduleA, type: 'memory' }],
      configDir: tempDir,
    });

    expect(runtime.startedModuleIds).toEqual(['module-a']);
    expect(runtime.reports.startup?.status).toBe('success');

    await runtime.stop();
  });

  it('marks invalid url source as discover rejection and continues with memory module', async () => {
    const baseConfig = {
      platform: { startupPolicy: 'strict' },
      modules: { artifactCache: { enabled: false } },
    } as IPlatformConfig;

    writeFileSync(
      join(tempDir, 'app_settings.json'),
      JSON.stringify(baseConfig),
    );

    const manifestA = createManifest({ id: 'module-a' });
    const moduleA = new TestModule();

    const runtime = await createRuntime({
      modules: [
        { manifest: manifestA, module: moduleA, type: 'memory' },
        {
          moduleIdHint: 'module-url',
          type: 'url',
          url: 'http://insecure.example/module.zip',
          packaging: 'zip',
        },
      ],
      configDir: tempDir,
    });

    expect(runtime.startedModuleIds).toEqual(['module-a']);
    expect(runtime.reports.startup?.status).toBe('degraded');
    expect(
      runtime.reports.startup?.failedModules.some(
        (item) => item.errorCode === RuntimeErrorCodes.SourceUrlInvalid,
      ),
    ).toBe(true);
    expect(
      runtime.reports.startup?.skippedModules.some(
        (item) => item.moduleId === 'module-url',
      ),
    ).toBe(true);

    await runtime.stop();
  });

  it('validates path checksum and rejects on integrity mismatch', async () => {
    const baseConfig = {
      platform: { startupPolicy: 'strict' },
      modules: { artifactCache: { enabled: false } },
    } as IPlatformConfig;

    writeFileSync(
      join(tempDir, 'app_settings.json'),
      JSON.stringify(baseConfig),
    );

    const artifactTempDir = await mkdtemp(join(tmpdir(), 'prosto-loader-'));
    const artifactPath = join(artifactTempDir, 'module.zip');

    try {
      await writeFile(artifactPath, 'artifact payload', 'utf8');

      const runtime = await createRuntime({
        modules: [
          {
            moduleIdHint: 'module-path',
            type: 'path',
            path: artifactPath,
            packaging: 'zip',
            integrity: {
              checksum:
                'sha256:0000000000000000000000000000000000000000000000000000000000000000',
            },
          },
        ],
        configDir: tempDir,
      });

      expect(runtime.startedModuleIds).toEqual([]);
      expect(
        runtime.reports.startup?.failedModules.some(
          (item) =>
            item.errorCode === RuntimeErrorCodes.SourceIntegrityMismatch,
        ),
      ).toBe(true);

      await runtime.stop();
    } finally {
      await rm(artifactTempDir, { recursive: true, force: true });
    }
  });

  it('passes path checksum preflight and reports entry resolve as not implemented', async () => {
    const baseConfig = {
      platform: { startupPolicy: 'strict' },
      modules: { artifactCache: { enabled: false } },
    } as IPlatformConfig;

    writeFileSync(
      join(tempDir, 'app_settings.json'),
      JSON.stringify(baseConfig),
    );

    const artifactTempDir = await mkdtemp(join(tmpdir(), 'prosto-loader-'));
    const artifactPath = join(artifactTempDir, 'module.zip');

    try {
      await writeFile(artifactPath, 'artifact payload', 'utf8');
      const checksum = createHash('sha256')
        .update('artifact payload')
        .digest('hex');

      const runtime = await createRuntime({
        modules: [
          {
            moduleIdHint: 'module-path-ok',
            type: 'path',
            path: artifactPath,
            packaging: 'zip',
            integrity: {
              checksum: `sha256:${checksum}`,
            },
          },
        ],
        configDir: tempDir,
      });

      expect(runtime.startedModuleIds).toEqual([]);
      expect(
        runtime.reports.startup?.failedModules.some(
          (item) => item.errorCode === RuntimeErrorCodes.SourceExtractionFailed,
        ),
      ).toBe(true);

      await runtime.stop();
    } finally {
      await rm(artifactTempDir, { recursive: true, force: true });
    }
  });
});
