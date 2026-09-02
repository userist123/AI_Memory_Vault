import type { IPlatformConfig } from '@/runtime/index.js';
import { PlatformRuntime } from '@/runtime/index.js';
import { describe, expect, it } from 'vitest';
import type { IBootstrapCoordinator } from '@/bootstrap/index.js';
import type {
  IDiagnosticsReporter,
  IRuntimeShutdownReport,
  IRuntimeStartupReport,
  IShutdownReportInput,
  IStartupReportInput,
} from '@/diagnostics/index.js';
import { RuntimeStartupStatus } from '@/diagnostics/index.js';
import {
  type IModuleLifecycleOrchestrator,
  ModuleState,
} from '@/modularity/index.js';
import { createManifest, TestModule } from '@/tests/fixtures/index.js';

class TestDiagnosticsReporter implements IDiagnosticsReporter {
  createStartupReport(input: IStartupReportInput): IRuntimeStartupReport {
    return {
      type: 'startup',
      status: RuntimeStartupStatus.Success,
      degraded: false,
      correlationId: input.correlationId,
      startedAt: input.startedAt,
      completedAt: input.startedAt,
      policyMode: input.policyMode,
      loadedModules: [...input.loadedModules],
      skippedModules: [...input.skippedModules],
      failedModules: [...input.failedModules],
    };
  }

  createShutdownReport(input: IShutdownReportInput): IRuntimeShutdownReport {
    return {
      type: 'shutdown',
      correlationId: input.correlationId,
      startedAt: input.startedAt,
      completedAt: input.startedAt,
      stopOrder: [...input.stopOrder],
      issues: [...input.issues],
    };
  }
}

describe('PlatformRuntime', () => {
  it('starts and stops runtime via injected collaborators', async () => {
    const manifest = createManifest({ id: 'module-a' });
    const module = new TestModule();

    const bootstrapCoordinator: IBootstrapCoordinator = {
      async coordinate() {
        return {
          policyMode: 'strict',
          loadedModules: [
            {
              manifest,
              module,
              fullPhysicalPath: '',
              state: ModuleState.ReadyForInitialization,
            },
          ],
          skippedModuleIds: [],
          failedDiagnostics: [],
          stageOutcomes: [],
        };
      },
    };

    const lifecycleOrchestrator: IModuleLifecycleOrchestrator = {
      async initializeModules(loadedModules) {
        return { initializedModules: loadedModules, issues: [] };
      },
      async startModules(initializedModules) {
        return { startedModules: initializedModules, issues: [] };
      },
      async stopModules(startedModules) {
        return {
          stopOrder: startedModules.map((item) => item.manifest.id).reverse(),
          issues: [],
        };
      },
    };

    const serviceRegistry = {
      has: () => false,
      register: () => {
        /* noop */
      },
      override: () => {
        /* noop */
      },
      resolve: () => {
        return {} as never;
      },
      resolveRequired: () => {
        return {} as never;
      },
      unregister: () => {
        /* noop */
      },
    };

    const runtime = new PlatformRuntime(
      [{ type: 'memory', manifest, module }],
      {
        platform: { startupPolicy: 'strict' },
        modules: { artifactCache: { enabled: false } },
        runtime: { shutdownTimeoutMs: 30000 },
      } as IPlatformConfig,
      new TestDiagnosticsReporter(),
      bootstrapCoordinator,
      lifecycleOrchestrator,
      serviceRegistry,
    );

    await runtime.start();

    expect(runtime.started).toBe(true);
    expect(runtime.stopped).toBe(false);
    expect(runtime.startedModuleIds).toEqual(['module-a']);
    expect(runtime.reports.startup?.status).toBe(RuntimeStartupStatus.Success);

    await runtime.stop();

    expect(runtime.stopped).toBe(true);
    expect(runtime.started).toBe(false);
    expect(runtime.reports.shutdown?.stopOrder).toEqual(['module-a']);
  });
});
