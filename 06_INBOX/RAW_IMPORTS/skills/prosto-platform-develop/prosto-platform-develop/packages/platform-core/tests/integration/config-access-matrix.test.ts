import type { IPlatformConfig, IPlatformRuntime } from '@/runtime/index.js';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { validateOperationalReportsSchema } from '@/diagnostics/index.js';
import {
  createManifest,
  createRuntime,
  TestModule,
} from '@/tests/fixtures/index.js';

describe('config access policy matrix', () => {
  let tempDir: string;

  beforeAll(() => {
    tempDir = mkdtempSync(join(tmpdir(), 'prosto-access-matrix-test-'));
  });

  afterAll(() => {
    rmSync(tempDir, { recursive: true, force: true });
  });

  /*
  function hasAnyReasonCode(
    runtime: IPlatformRuntime,
    code: `${RuntimeErrorCodes}`,
  ): boolean {
    const failed =
      runtime.reports.startup?.failedModules.some(
        (f) => f.errorCode === code,
      ) ?? false;

    const skipped =
      runtime.reports.startup?.skippedModules.some(
        (s) => s.reason.errorCode === code,
      ) ?? false;

    return failed || skipped;
  }
  */

  async function createTestRuntime(
    config: Partial<IPlatformConfig>,
    moduleId: string,
  ) {
    const fullConfig = {
      platform: { startupPolicy: 'strict' as const },
      modules: { artifactCache: { enabled: false } },
      ...config,
    } as IPlatformConfig;

    writeFileSync(
      join(tempDir, 'app_settings.json'),
      JSON.stringify(fullConfig),
    );

    const manifest = createManifest({ id: moduleId });
    const module = new TestModule();

    const runtime = await createRuntime({
      modules: [{ manifest, module, type: 'memory' }],
      correlationId: `matrix-test-${moduleId}`,
      configDir: tempDir,
    });

    return runtime;
  }

  function wasModuleAllowed(runtime: IPlatformRuntime) {
    const loadedModules = runtime.reports.startup?.loadedModules ?? [];
    return loadedModules.length > 0;
  }

  it('trusted class can access module-scoped configuration', async () => {
    const runtime = await createTestRuntime(
      { platform: { startupPolicy: 'strict' } } as IPlatformConfig,
      'module-trusted-scoped',
    );

    expect(() =>
      validateOperationalReportsSchema(runtime.reports),
    ).not.toThrow();

    expect(wasModuleAllowed(runtime)).toBe(true);

    await runtime.stop();
  });

  /*
  it('internal class cannot access unauthorized global sections', async () => {
    const runtime = await createTestRuntime(
      { platform: { startupPolicy: 'strict' } } as IPlatformConfig,
      'module-internal-unauthorized',
      ['lifecycle.register', 'config.read.modules'],
      'internal',
    );

    expect(() =>
      validateOperationalReportsSchema(runtime.reports),
    ).not.toThrow();
    expect(
      hasAnyReasonCode(runtime, RuntimeErrorCodes.ConfigSectionNotAllowlisted),
    ).toBe(true);

    await runtime.stop();
  });

  it('third-party-reviewed class denied on unknown capability', async () => {
    const runtime = await createTestRuntime(
      { platform: { startupPolicy: 'strict' } } as IPlatformConfig,
      'module-thirdparty-unknown',
      ['lifecycle.register', 'config.read.unknown_capability'],
      'third-party-reviewed',
    );

    expect(
      hasAnyReasonCode(runtime, RuntimeErrorCodes.ConfigCapabilityInvalid),
    ).toBe(true);

    await runtime.stop();
  });

  it('third-party-reviewed class denied due to section not allowlisted', async () => {
    const runtime = await createTestRuntime(
      { platform: { startupPolicy: 'strict' } } as IPlatformConfig,
      'module-prod-strict',
      ['lifecycle.register', 'config.read.modules'],
      'third-party-reviewed',
    );

    expect(runtime.reports.startup?.policyMode).toBe('strict');
    expect(
      hasAnyReasonCode(runtime, RuntimeErrorCodes.ConfigSectionNotAllowlisted),
    ).toBe(true);

    await runtime.stop();
  });

  it('strict mode enforces wildcard prohibition', async () => {
    const runtime = await createTestRuntime(
      { platform: { startupPolicy: 'strict' } } as IPlatformConfig,
      'module-wildcard-test',
      ['lifecycle.register', 'config.read.*'], // Wildcard capability
      'internal',
    );

    const hasWildcardError =
      runtime.reports.startup?.failedModules.some((f) =>
        f.message.includes('wildcard_config_capability_forbidden'),
      ) ||
      runtime.reports.startup?.skippedModules.some((s) =>
        s.reason.message.includes('wildcard_config_capability_forbidden'),
      );

    expect(hasWildcardError).toBe(true);

    await runtime.stop();
  });
  */
});
