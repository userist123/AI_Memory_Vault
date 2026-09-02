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

describe('runtime shutdown', () => {
  let tempDir: string;

  beforeAll(() => {
    tempDir = mkdtempSync(join(tmpdir(), 'prosto-config-test-'));
  });

  afterAll(() => {
    rmSync(tempDir, { recursive: true, force: true });
  });

  it('stops modules in reverse startup order', async () => {
    const baseConfig = {
      platform: { startupPolicy: 'strict' },
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
    });
    const moduleA = new TestModule();
    const moduleB = new TestModule();

    const runtime = await createRuntime({
      modules: [
        { manifest: manifestB, module: moduleB, type: 'memory' },
        { manifest: manifestA, module: moduleA, type: 'memory' },
      ],
      configDir: tempDir,
    });

    await runtime.stop();

    expect(runtime.reports.shutdown?.stopOrder).toEqual([
      'module-b',
      'module-a',
    ]);
    expect(runtime.reports.shutdown?.issues).toEqual([]);
  });
});
