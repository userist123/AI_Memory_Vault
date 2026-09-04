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

describe('runtime bootstrap critical failure', () => {
  let tempDir: string;

  beforeAll(() => {
    tempDir = mkdtempSync(join(tmpdir(), 'prosto-config-test-'));
  });

  afterAll(() => {
    rmSync(tempDir, { recursive: true, force: true });
  });

  it('aborts startup even in best-effort mode when critical module fails', async () => {
    const baseConfig = {
      platform: { startupPolicy: 'best-effort' },
      modules: { artifactCache: { enabled: false } },
    } as IPlatformConfig;

    writeFileSync(
      join(tempDir, 'app_settings.json'),
      JSON.stringify(baseConfig),
    );

    const criticalManifest = createManifest({ id: 'module-critical' });
    const critical = new TestModule({ failOnStart: true });

    const nonCriticalManifest = createManifest({
      id: 'module-standard',
      optional: true,
    });
    const nonCritical = new TestModule();

    const runtime = await createRuntime({
      modules: [
        { manifest: nonCriticalManifest, module: nonCritical, type: 'memory' },
        { manifest: criticalManifest, module: critical, type: 'memory' },
      ],
      configDir: tempDir,
    });

    expect(runtime.reports.startup?.status).toBe('failed');
    expect(runtime.startedModuleIds).toEqual([]);
    expect(
      runtime.reports.startup?.failedModules.some(
        (item) => item.moduleId === 'module-critical',
      ),
    ).toBe(true);

    await runtime.stop();
  });
});
