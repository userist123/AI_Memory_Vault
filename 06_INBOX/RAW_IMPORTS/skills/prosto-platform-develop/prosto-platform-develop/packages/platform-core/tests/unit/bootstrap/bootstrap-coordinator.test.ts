import {
  BootstrapCoordinator,
  BootstrapPipeline,
  DiscoverStage,
  ModulesInitializationStage,
  ModulesStartStage,
  PersistenceInitializationStage,
  ResolveDependenciesStage,
  ValidateStage,
} from '@/bootstrap/index.js';
import { InMemoryEventBus } from '@/events/index.js';
import { ConsoleModuleLoggerFactory } from '@/logging/index.js';
import {
  BestEffortPolicyStrategy,
  ManifestValidationStrategy,
  ModuleContextFactory,
  ModuleLifecycleOrchestrator,
  ModuleLoader,
  StartupPolicyEvaluator,
  StrictPolicyStrategy,
} from '@/modularity/index.js';
import type { IPlatformConfig } from '@/runtime/index.js';
import { InMemoryServiceRegistry } from '@/services/index.js';
import { createManifest, TestModule } from '@/tests/fixtures/index.js';
import { describe, expect, it } from 'vitest';

describe('BootstrapCoordinator', () => {
  it('coordinates discover -> validate -> resolve -> lifecycle and starts modules', async () => {
    const manifest = createManifest({ id: 'module-a' });
    const module = new TestModule();

    const moduleLoader = new ModuleLoader();

    const startupPolicyEvaluator = new StartupPolicyEvaluator([
      new StrictPolicyStrategy(),
      new BestEffortPolicyStrategy(),
    ]);

    const serviceRegistry = new InMemoryServiceRegistry();
    const eventBus = new InMemoryEventBus();
    const moduleContextFactory = new ModuleContextFactory(
      'production',
      {} as IPlatformConfig,
      eventBus,
      serviceRegistry,
      new ConsoleModuleLoggerFactory(),
    );

    const moduleLifecycleOrchestrator = new ModuleLifecycleOrchestrator(
      moduleContextFactory,
    );

    const bootstrapCoordinator = new BootstrapCoordinator(
      BootstrapPipeline.create([
        new DiscoverStage(moduleLoader),
        new ValidateStage([new ManifestValidationStrategy()]),
        new ResolveDependenciesStage(startupPolicyEvaluator),
        new ModulesInitializationStage(
          startupPolicyEvaluator,
          moduleLifecycleOrchestrator,
        ),
        new PersistenceInitializationStage(),
        new ModulesStartStage(
          startupPolicyEvaluator,
          moduleLifecycleOrchestrator,
        ),
      ]),
    );

    const result = await bootstrapCoordinator.coordinate({
      policyMode: 'strict',
      runtimeVersion: {
        sdkVersion: '0.0.0',
        nodeVersion: process.versions.node,
      },
      modules: [{ type: 'memory', manifest, module }],
      correlationId: 'cid',
      startupStartedAt: '2026-01-01T00:00:00.000Z',
      services: serviceRegistry,
    });

    expect(result.loadedModules.map((item) => item.manifest.id)).toEqual([
      'module-a',
    ]);
    expect(result.failedDiagnostics).toEqual([]);
    expect(result.stageOutcomes).toHaveLength(6);
  });
});
