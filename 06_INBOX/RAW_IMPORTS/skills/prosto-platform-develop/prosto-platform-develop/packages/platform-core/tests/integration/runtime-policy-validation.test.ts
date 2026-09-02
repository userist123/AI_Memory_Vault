import type { IPlatformConfig } from '@/runtime/index.js';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { validateOperationalReportsSchema } from '@/diagnostics/index.js';
import {
  createManifest,
  createRuntime,
  TestModule,
} from '@/tests/fixtures/index.js';

describe('runtime policy diagnostics validation', () => {
  let tempDir: string;

  beforeAll(() => {
    tempDir = mkdtempSync(join(tmpdir(), 'prosto-config-test-'));
  });

  afterAll(() => {
    rmSync(tempDir, { recursive: true, force: true });
  });

  it('produces startup diagnostics payload with required fields', async () => {
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
      correlationId: 'rt-validation-test',
      configDir: tempDir,
    });

    expect(() =>
      validateOperationalReportsSchema(runtime.reports),
    ).not.toThrow();
    expect(runtime.reports.startup?.correlationId).toBe('rt-validation-test');
    expect(runtime.reports.startup?.policyMode).toBe('strict');

    await runtime.stop();
  });
});
