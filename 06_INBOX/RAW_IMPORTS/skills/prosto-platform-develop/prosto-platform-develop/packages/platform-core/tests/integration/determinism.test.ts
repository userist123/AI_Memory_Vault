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

describe('runtime determinism', () => {
  let tempDir: string;

  beforeAll(() => {
    tempDir = mkdtempSync(join(tmpdir(), 'prosto-config-test-'));
  });

  afterAll(() => {
    rmSync(tempDir, { recursive: true, force: true });
  });

  it('produces identical startup order for identical module set across repeated runs', async () => {
    const baseConfig = {
      platform: { startupPolicy: 'strict' },
      modules: { artifactCache: { enabled: false } },
    } as IPlatformConfig;

    writeFileSync(
      join(tempDir, 'app_settings.json'),
      JSON.stringify(baseConfig),
    );

    const createRuntimeInstance = async () => {
      const manifestA = createManifest({ id: 'module-a' });
      const manifestB = createManifest({
        id: 'module-b',
        dependencies: [{ id: 'module-a', version: '^1.0.0' }],
      });
      const manifestC = createManifest({
        id: 'module-c',
        dependencies: [{ id: 'module-b', version: '^1.0.0' }],
      });
      const moduleA = new TestModule();
      const moduleB = new TestModule();
      const moduleC = new TestModule();

      return createRuntime({
        modules: [
          { manifest: manifestC, module: moduleC, type: 'memory' },
          { manifest: manifestA, module: moduleA, type: 'memory' },
          { manifest: manifestB, module: moduleB, type: 'memory' },
        ],
        configDir: tempDir,
      });
    };

    const runs = await Promise.all([
      createRuntimeInstance(),
      createRuntimeInstance(),
      createRuntimeInstance(),
    ]);
    const orders = runs.map((runtime) => runtime.startedModuleIds.join(','));

    expect(new Set(orders).size).toBe(1);
    expect(orders[0]).toBe('module-a,module-b,module-c');

    runs.forEach((runtime) => runtime.stop());
  });
});
