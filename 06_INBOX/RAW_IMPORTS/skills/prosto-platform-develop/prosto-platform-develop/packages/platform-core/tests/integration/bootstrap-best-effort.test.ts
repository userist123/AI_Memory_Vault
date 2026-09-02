import type { IPlatformConfig } from '@/runtime/index.js';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import {
  createManifest,
  createRuntime,
  TestModule,
} from '@/tests/fixtures/index.js';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';

describe('runtime bootstrap (best-effort)', () => {
  let tempDir: string;

  beforeAll(() => {
    tempDir = mkdtempSync(join(tmpdir(), 'prosto-config-test-'));
  });

  afterAll(() => {
    rmSync(tempDir, { recursive: true, force: true });
  });

  it('skips non-critical failing module and starts in degraded mode', async () => {
    const baseConfig = {
      platform: { startupPolicy: 'best-effort' },
      modules: { artifactCache: { enabled: false } },
    } as IPlatformConfig;

    writeFileSync(
      join(tempDir, 'app_settings.json'),
      JSON.stringify(baseConfig),
    );

    const manifestA = createManifest({ id: 'module-a' });
    const manifestB = createManifest({
      id: 'module-b',
      dependencies: [{ id: 'module-a', version: '^1.0.0' }],
      optional: true,
    });
    const moduleA = new TestModule();
    const moduleB = new TestModule({ failOnStart: true });

    const runtime = await createRuntime({
      modules: [
        { manifest: manifestB, module: moduleB, type: 'memory' },
        { manifest: manifestA, module: moduleA, type: 'memory' },
      ],
      configDir: tempDir,
    });

    expect(runtime.reports.startup?.status).toBe('degraded');
    expect(runtime.reports.startup?.degraded).toBe(true);
    expect(runtime.startedModuleIds).toEqual(['module-a']);
    expect(
      runtime.reports.startup?.skippedModules.some(
        (item) => item.moduleId === 'module-b',
      ),
    ).toBe(true);
    expect(
      runtime.reports.startup?.failedModules.some(
        (item) => item.moduleId === 'module-b',
      ),
    ).toBe(true);

    await runtime.stop();
  });
});
