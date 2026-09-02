import type { IPlatformModuleContext } from '@prosto/platform-sdk';
import { describe, expect, it } from 'vitest';
import { ModuleContextFactory } from '@/modularity/index.js';
import { InMemoryEventBus } from '@/events/index.js';
import { ConsoleModuleLoggerFactory } from '@/logging/index.js';
import type { IPlatformConfig } from '@/runtime/index.js';
import { InMemoryServiceRegistry } from '@/services/index.js';
import { createManifest } from '@/tests/fixtures/index.js';

const MINIMAL_CONFIG: IPlatformConfig = {
  platform: {
    name: 'test',
    version: '0.0.0',
    basePath: '/tmp',
    startupPolicy: 'strict',
  },
  runtime: { shutdownTimeoutMs: 10000 },
  persistence: { typeorm: { enabled: false } },
  logging: { level: 'info', format: 'text' },
  modules: {
    'module-a': {
      database: { host: 'localhost', port: 5432 },
      features: { enableLogging: true },
    },
    'module-b': {
      cache: { ttl: 300 },
    },
    configAccessPolicy: {
      productionStrictMode: true,
    },
    artifactCache: { enabled: false },
  },
  security: {
    secretRedaction: {
      enabled: true,
      patterns: ['key'],
    },
  },
  custom: {},
};

function createFactory(
  config = MINIMAL_CONFIG,
  environment = 'production',
): ModuleContextFactory {
  return new ModuleContextFactory(
    environment,
    config,
    new InMemoryEventBus(),
    new InMemoryServiceRegistry(),
    new ConsoleModuleLoggerFactory(),
  );
}

describe('ModuleContextFactory', () => {
  it('includes module-scoped config without global sections when no config capabilities', () => {
    const factory = createFactory();
    const ctx: IPlatformModuleContext = factory.create({
      startupPolicy: 'strict',
      sdkVersion: '0.0.0',
      moduleManifest: createManifest({ id: 'module-a' }),
      lifecycleStage: 'init',
      persistenceEnabled: false,
    });

    expect(ctx.config).toBeDefined();
    expect(ctx.config?.modules).toBeDefined();
    expect(ctx.config?.modules?.['module-a']).toEqual({
      database: { host: 'localhost', port: 5432 },
      features: { enableLogging: true },
    });
    expect(ctx.config?.modules?.['module-b']).toBeDefined();
    expect(ctx.config?.security).toBeDefined();
  });

  it('includes allowed global sections for modules with config capabilities', () => {
    const factory = createFactory();
    const ctx: IPlatformModuleContext = factory.create({
      startupPolicy: 'strict',
      sdkVersion: '0.0.0',
      moduleManifest: createManifest({ id: 'module-a' }),
      lifecycleStage: 'init',
      persistenceEnabled: false,
    });

    expect(ctx.config?.platform).toBeDefined();
    expect(ctx.config?.platform?.name).toBe('test');
    expect(ctx.config?.security).toBeDefined();
    expect(ctx.config?.modules?.['module-a']).toBeDefined();
  });

  it('denies cross-class sections to lower security classes', () => {
    const factory = createFactory();
    const ctx: IPlatformModuleContext = factory.create({
      startupPolicy: 'strict',
      sdkVersion: '0.0.0',
      moduleManifest: createManifest({ id: 'module-a' }),
      lifecycleStage: 'init',
      persistenceEnabled: false,
    });

    expect(ctx.config?.custom).toBeDefined();
    expect(ctx.config?.modules?.['module-a']).toBeDefined();
  });

  it('provides getConfigValue that works with scoped config', () => {
    const factory = createFactory();
    const ctx: IPlatformModuleContext = factory.create({
      startupPolicy: 'strict',
      sdkVersion: '0.0.0',
      moduleManifest: createManifest({ id: 'module-a' }),
      lifecycleStage: 'init',
      persistenceEnabled: false,
    });

    expect(ctx.getConfigValue('modules.module-a.database.host')).toBe(
      'localhost',
    );
    expect(ctx.getConfigValue('modules.module-a.database.port')).toBe(5432);
    expect(ctx.getConfigValue('missing.key', 'fallback')).toBe('fallback');
    expect(ctx.getConfigValue('security.secretRedaction.enabled')).toBe(true);
  });

  it('exposes logger, eventBus, services and metadata in context', () => {
    const factory = createFactory();
    const ctx: IPlatformModuleContext = factory.create({
      startupPolicy: 'best-effort',
      sdkVersion: '1.0.0',
      moduleManifest: createManifest({ id: 'module-a' }),
      lifecycleStage: 'init',
      persistenceEnabled: false,
    });

    expect(ctx.moduleId).toBe('module-a');
    expect(ctx.sdkVersion).toBe('1.0.0');
    expect(ctx.startupPolicy).toBe('best-effort');
    expect(ctx.logger).toBeDefined();
    expect(ctx.eventBus).toBeDefined();
    expect(ctx.services).toBeDefined();
  });
});
