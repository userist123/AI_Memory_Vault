import { afterEach, describe, expect, it } from 'vitest';
import { RuntimeStartupStatus } from '@/diagnostics/index.js';
import type {
  IPlatformConfig,
  IRuntimeBuilderOptions,
} from '@/runtime/index.js';
import { RuntimeBuilder } from '@/runtime/index.js';
import { createManifest, TestModule } from '@/tests/fixtures/index.js';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

class TestRuntimeBuilder extends RuntimeBuilder {
  buildConfig(options: IRuntimeBuilderOptions): IPlatformConfig {
    return this._buildPlatformConfig(options);
  }
}

describe('RuntimeBuilder', () => {
  const tempDirs: string[] = [];

  afterEach(() => {
    for (const tempDir of tempDirs.splice(0)) {
      rmSync(tempDir, { recursive: true, force: true });
    }
  });

  it('builds runtime and wires startup/shutdown flow', async () => {
    const runtime = new RuntimeBuilder().build({
      modules: [
        {
          type: 'memory',
          manifest: createManifest({ id: 'module-a' }),
          module: new TestModule(),
        },
      ],
    });

    await runtime.start();

    expect(runtime.started).toBe(true);
    expect(runtime.stopped).toBe(false);
    expect(runtime.startedModuleIds).toEqual(['module-a']);
    expect(runtime.reports.startup?.status).toEqual(
      RuntimeStartupStatus.Success,
    );

    await runtime.stop();

    expect(runtime.stopped).toBe(true);
    expect(runtime.started).toBe(false);
    expect(runtime.reports.shutdown?.stopOrder).toEqual(['module-a']);
  });

  it('merges package defaults, deployment files, local override, environment, and CLI in order', () => {
    const configDir = mkdtempSync(join(tmpdir(), 'prosto-runtime-config-'));

    tempDirs.push(configDir);

    writeFileSync(
      join(configDir, 'app_settings.json'),
      JSON.stringify({ logging: { level: 'warn' } }),
    );

    writeFileSync(
      join(configDir, 'app_settings.test.json'),
      JSON.stringify({ logging: { level: 'error' } }),
    );

    writeFileSync(
      join(configDir, 'app_settings.local.json'),
      JSON.stringify({
        persistence: { typeorm: { password: 'local-secret' } },
      }),
    );

    const previousValue = process.env['PROSTO_LOGGING__LEVEL'];

    process.env['PROSTO_LOGGING__LEVEL'] = 'debug';

    try {
      const config = new TestRuntimeBuilder().buildConfig({
        configDir,
        environment: 'test',
        commandLineArgs: ['--logging:level=trace'],
      });

      expect(config.logging.level).toBe('trace');
      expect(config.persistence.typeorm.password).toBe('local-secret');
      expect(config.persistence.typeorm.synchronize).toBe(false);
    } finally {
      if (previousValue === undefined) {
        delete process.env['PROSTO_LOGGING__LEVEL'];
      } else {
        process.env['PROSTO_LOGGING__LEVEL'] = previousValue;
      }
    }
  });

  it('rejects local overrides outside supported persistence settings', () => {
    const configDir = mkdtempSync(join(tmpdir(), 'prosto-runtime-config-'));

    tempDirs.push(configDir);

    writeFileSync(
      join(configDir, 'app_settings.local.json'),
      JSON.stringify({ logging: { level: 'debug' } }),
    );

    expect(() => new TestRuntimeBuilder().buildConfig({ configDir })).toThrow();
  });
});
