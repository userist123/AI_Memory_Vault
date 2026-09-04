import { describe, expect, it, beforeAll, afterAll } from 'vitest';
import {
  ConfigurationBuilder,
  ConfigurationValidator,
} from '@/common/index.js';
import { type IPlatformConfig, platformConfigSchema } from '@/runtime/index.js';
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';

describe('Config Integration', () => {
  let tempDir: string;
  const configValidator = new ConfigurationValidator();

  beforeAll(() => {
    tempDir = mkdtempSync(join(tmpdir(), 'prosto-config-test-'));
  });

  afterAll(() => {
    rmSync(tempDir, { recursive: true, force: true });
  });

  it('builds config from files with environment overrides', () => {
    const baseConfig: IPlatformConfig = {
      platform: {
        name: 'prosto-platform',
        version: '1.0.0',
        basePath: process.cwd(),
        startupPolicy: 'strict',
      },
      runtime: { shutdownTimeoutMs: 30000 },
      persistence: { typeorm: { enabled: false } },
      modules: {
        configAccessPolicy: {
          productionStrictMode: true,
        },
        artifactCache: { enabled: false, path: './.cache' },
      },
      security: { secretRedaction: { enabled: true, patterns: ['password'] } },
      logging: { level: 'info', format: 'text' },
      custom: {},
    };

    writeFileSync(
      join(tempDir, 'app_settings.json'),
      JSON.stringify(baseConfig),
    );
    writeFileSync(
      join(tempDir, 'app_settings.production.json'),
      JSON.stringify({
        logging: { level: 'error' },
        runtime: { shutdownTimeoutMs: 60000 },
      } as IPlatformConfig),
    );

    const rawConfig = new ConfigurationBuilder()
      .addJsonFile(join(tempDir, 'app_settings.json'))
      .addJsonFile(join(tempDir, 'app_settings.production.json'))
      .addCommandLine(['--platform:startupPolicy=best-effort'])
      .build();

    const validated = configValidator.validate(rawConfig, platformConfigSchema);

    expect(validated.platform.name).toBe('prosto-platform');
    expect(validated.runtime.shutdownTimeoutMs).toBe(60000);
    expect(validated.logging.level).toBe('error');
    expect(validated.logging.format).toBe('text');
    expect(validated.platform.startupPolicy).toBe('best-effort');
  });

  it('handles missing environment file as optional', () => {
    const baseConfig: IPlatformConfig = {
      platform: {
        name: 'test',
        version: '0.1.0',
        basePath: process.cwd(),
        startupPolicy: 'strict',
      },
      runtime: { shutdownTimeoutMs: 5000 },
      persistence: { typeorm: { enabled: false } },
      modules: {
        configAccessPolicy: {
          productionStrictMode: true,
        },
        artifactCache: { enabled: false, path: './.cache' },
      },
      security: { secretRedaction: { enabled: true, patterns: [] } },
      logging: { level: 'info', format: 'text' },
      custom: {},
    };

    writeFileSync(join(tempDir, 'base_only.json'), JSON.stringify(baseConfig));

    const rawConfig = new ConfigurationBuilder()
      .addJsonFile(join(tempDir, 'base_only.json'))
      .addJsonFile(join(tempDir, 'nonexistent.json'), { optional: true })
      .build();

    const validated = configValidator.validate(rawConfig, platformConfigSchema);

    expect(validated.platform.name).toBe('test');
  });

  it('command line overrides nested config values', () => {
    const baseConfig: IPlatformConfig = {
      platform: {
        name: 'test',
        version: '0.1.0',
        basePath: process.cwd(),
        startupPolicy: 'strict',
      },
      runtime: { shutdownTimeoutMs: 5000 },
      persistence: { typeorm: { enabled: false } },
      modules: {
        configAccessPolicy: {
          productionStrictMode: true,
        },
        artifactCache: { enabled: false, path: './.cache' },
      },
      security: { secretRedaction: { enabled: true, patterns: [] } },
      logging: { level: 'info', format: 'text' },
      custom: {},
    };

    const rawConfig = new ConfigurationBuilder()
      .addInMemoryCollection(baseConfig)
      .addCommandLine([
        '--logging:level=debug',
        '--modules:artifactCache:enabled=true',
      ])
      .build();

    const validated = configValidator.validate(rawConfig, platformConfigSchema);

    expect(validated.logging.level).toBe('debug');
    expect(validated.modules.artifactCache.enabled).toBe(true);
    expect(validated.runtime.shutdownTimeoutMs).toBe(5000);
  });

  it('environment variables override file config', () => {
    const originalEnv = process.env['PROSTO_LOGGING__LEVEL'];
    process.env['PROSTO_LOGGING__LEVEL'] = 'debug';

    const baseConfig: IPlatformConfig = {
      platform: {
        name: 'test',
        version: '0.1.0',
        basePath: process.cwd(),
        startupPolicy: 'strict',
      },
      runtime: { shutdownTimeoutMs: 5000 },
      persistence: { typeorm: { enabled: false } },
      modules: {
        configAccessPolicy: {
          productionStrictMode: true,
        },
        artifactCache: { enabled: false, path: './.cache' },
      },
      security: { secretRedaction: { enabled: true, patterns: [] } },
      logging: { level: 'info', format: 'text' },
      custom: {},
    };

    const rawConfig = new ConfigurationBuilder()
      .addInMemoryCollection(baseConfig)
      .addEnvironmentVariables({ prefix: 'PROSTO_' })
      .build();

    const validated = configValidator.validate(rawConfig, platformConfigSchema);

    expect(validated.logging.level).toBe('debug');
    expect(validated.logging.format).toBe('text');

    if (originalEnv !== undefined) {
      process.env['PROSTO_LOGGING__LEVEL'] = originalEnv;
    } else {
      delete process.env['PROSTO_LOGGING__LEVEL'];
    }
  });
});
