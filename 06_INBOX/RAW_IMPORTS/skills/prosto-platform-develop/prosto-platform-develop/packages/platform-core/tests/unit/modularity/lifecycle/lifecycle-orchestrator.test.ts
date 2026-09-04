import type { IPlatformConfig } from '@/runtime/index.js';
import type {
  IPersistenceInitializationInput,
  IPersistenceProvider,
  PersistenceProviderStateType,
} from '@prosto/platform-sdk';
import { PersistenceDescriptorRegistry } from '@prosto/platform-sdk';
import { RuntimeErrorCodes } from '@/common/index.js';
import { describe, expect, it } from 'vitest';
import { InMemoryEventBus } from '@/events/index.js';
import { ConsoleModuleLoggerFactory } from '@/logging/index.js';
import {
  ModuleContextFactory,
  ModuleLifecycleOrchestrator,
  ModuleState,
} from '@/modularity/index.js';
import { InMemoryServiceRegistry } from '@/services/index.js';
import { createManifest, TestModule } from '@/tests/fixtures/index.js';

class FakePersistenceProvider implements IPersistenceProvider {
  readonly descriptors = new PersistenceDescriptorRegistry();
  readonly trace: string[];

  state: PersistenceProviderStateType = 'collecting';

  constructor(trace: string[]) {
    this.trace = trace;
  }

  async initialize(_input: IPersistenceInitializationInput): Promise<void> {
    this.trace.push('provider.initialize');
    this.state = 'ready';
  }

  async dispose(): Promise<void> {
    this.state = 'disposed';
  }
}

describe('ModuleLifecycleOrchestrator', () => {
  it('collects startup issues when module stage fails', async () => {
    const manifestA = createManifest({ id: 'module-a' });
    const manifestB = createManifest({ id: 'module-b' });
    const moduleA = new TestModule();
    const moduleB = new TestModule({ failOnStart: true });

    const serviceRegistry = new InMemoryServiceRegistry();
    const eventBus = new InMemoryEventBus();
    const contextFactory = new ModuleContextFactory(
      'production',
      {} as IPlatformConfig,
      eventBus,
      serviceRegistry,
      new ConsoleModuleLoggerFactory(),
    );

    const moduleLifecycleOrchestrator = new ModuleLifecycleOrchestrator(
      contextFactory,
    );
    const initialized = await moduleLifecycleOrchestrator.initializeModules(
      [
        {
          manifest: manifestA,
          module: moduleA,
          fullPhysicalPath: '',
          state: ModuleState.ReadyForInitialization,
        },
        {
          manifest: manifestB,
          module: moduleB,
          fullPhysicalPath: '',
          state: ModuleState.ReadyForInitialization,
        },
      ],
      {
        startupPolicy: 'strict',
        sdkVersion: '0.0.0',
      },
    );
    const result = await moduleLifecycleOrchestrator.startModules(
      initialized.initializedModules,
      {
        startupPolicy: 'strict',
        sdkVersion: '0.0.0',
      },
    );

    expect(result.startedModules.map((item) => item.manifest.id)).toEqual([
      'module-a',
    ]);
    expect(result.issues).toHaveLength(1);
    expect(result.issues[0]?.errorCode).toBe(
      RuntimeErrorCodes.LifecycleStartFailed,
    );
  });

  it('reports shutdown timeout issue', async () => {
    const manifest = createManifest({ id: 'module-slow' });
    const slowModule = new TestModule({ stopDelayMs: 50 });

    const serviceRegistry = new InMemoryServiceRegistry();
    const eventBus = new InMemoryEventBus();
    const contextFactory = new ModuleContextFactory(
      'production',
      {} as IPlatformConfig,
      eventBus,
      serviceRegistry,
      new ConsoleModuleLoggerFactory(),
    );

    const moduleLifecycleOrchestrator = new ModuleLifecycleOrchestrator(
      contextFactory,
    );
    const result = await moduleLifecycleOrchestrator.stopModules(
      [
        {
          manifest,
          module: slowModule,
          fullPhysicalPath: '',
          state: ModuleState.ReadyForInitialization,
        },
      ],
      {
        startupPolicy: 'strict',
        sdkVersion: '0.0.0',
        timeoutMs: 10,
      },
    );

    expect(result.stopOrder).toEqual(['module-slow']);
    expect(result.issues).toHaveLength(1);
    expect(result.issues[0]?.errorCode).toBe(RuntimeErrorCodes.ShutdownTimeout);
  });

  it('keeps persistence collection between init and start', async () => {
    const trace: string[] = [];
    const provider = new FakePersistenceProvider(trace);
    const manifestA = createManifest({ id: 'module-a' });
    const manifestB = createManifest({ id: 'module-b' });
    const moduleA = new TestModule();
    const moduleB = new TestModule({ failOnInit: true });

    moduleA.init = (context) => {
      trace.push('module-a.init');
      context.persistence?.descriptors?.register('module-a', {
        owner: 'module',
        ownerId: 'module-a',
        payload: {},
      });
    };
    moduleA.start = (context) => {
      trace.push('module-a.start');
      expect(context.persistence?.descriptors).toBeUndefined();
    };

    moduleB.init = (context) => {
      trace.push('module-b.init');
      context.persistence?.descriptors?.register('module-b', {
        owner: 'module',
        ownerId: 'module-b',
        payload: {},
      });
      throw new Error('init failed');
    };

    const orchestrator = new ModuleLifecycleOrchestrator(
      new ModuleContextFactory(
        'production',
        {} as IPlatformConfig,
        new InMemoryEventBus(),
        new InMemoryServiceRegistry(),
        new ConsoleModuleLoggerFactory(),
      ),
    );
    const initialized = await orchestrator.initializeModules(
      [
        {
          manifest: manifestA,
          module: moduleA,
          fullPhysicalPath: '',
          state: ModuleState.ReadyForInitialization,
        },
        {
          manifest: manifestB,
          module: moduleB,
          fullPhysicalPath: '',
          state: ModuleState.ReadyForInitialization,
        },
      ],
      {
        startupPolicy: 'best-effort',
        sdkVersion: '0.0.0',
        persistenceProvider: provider,
        persistenceEnabled: true,
      },
    );

    const descriptors = provider.descriptors.seal();

    await provider.initialize({
      descriptors,
      configuration: { typeorm: {} },
      services: new InMemoryServiceRegistry(),
    });

    await orchestrator.startModules(initialized.initializedModules, {
      startupPolicy: 'best-effort',
      sdkVersion: '0.0.0',
      persistenceProvider: provider,
      persistenceEnabled: true,
    });

    expect(descriptors).toHaveLength(1);
    expect(descriptors[0]?.ownerId).toBe('module-a');
    expect(trace).toEqual([
      'module-a.init',
      'module-b.init',
      'provider.initialize',
      'module-a.start',
    ]);
  });
});
