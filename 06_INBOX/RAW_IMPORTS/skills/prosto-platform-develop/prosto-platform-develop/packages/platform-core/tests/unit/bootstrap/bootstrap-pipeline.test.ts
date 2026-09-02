import { describe, expect, it } from 'vitest';
import {
  BootstrapPipeline,
  BootstrapStage,
  type IBootstrapStage,
  type IBootstrapStageContext,
} from '@/bootstrap/index.js';

function createInitialBootstrapStageContext(): IBootstrapStageContext {
  return {
    abort: false,
    stageOutcomes: [],
    loadedModules: [],
    failedDiagnostics: [],
    validatedModules: [],
    skippedModuleIds: new Set<string>(),
    policyMode: 'strict',
    correlationId: 'cid',
    startupStartedAt: '2026-01-01T00:00:00.000Z',
    runtimeVersion: {
      sdkVersion: '0.0.0',
      nodeVersion: process.versions.node,
    },
    moduleSources: [],
    preRejectedArtifacts: [],
    candidates: [],
    persistenceConfiguration: { typeorm: { enabled: false } },
    services: {
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
    },
  };
}

describe('BootstrapPipeline', () => {
  it('executes stages in deterministic order', async () => {
    const calls: string[] = [];

    const stages: IBootstrapStage[] = [
      {
        stageType: BootstrapStage.Discover,
        async execute(context) {
          calls.push('discover');
          return context;
        },
      },
      {
        stageType: BootstrapStage.Validate,
        async execute(context) {
          calls.push('validate');
          return context;
        },
      },
      {
        stageType: BootstrapStage.Resolve,
        async execute(context) {
          calls.push('resolve');
          return context;
        },
      },
    ];

    const pipeline = BootstrapPipeline.create(stages);

    await pipeline.execute(createInitialBootstrapStageContext());

    expect(calls).toEqual(['discover', 'validate', 'resolve']);
  });

  it('stops when stage sets abort flag', async () => {
    const calls: string[] = [];

    const stages: IBootstrapStage[] = [
      {
        stageType: BootstrapStage.Discover,
        async execute(context) {
          calls.push('discover');
          context.abort = true;
          return context;
        },
      },
      {
        stageType: BootstrapStage.Validate,
        async execute(context) {
          calls.push('validate');
          return context;
        },
      },
    ];

    const pipeline = BootstrapPipeline.create(stages);

    await pipeline.execute(createInitialBootstrapStageContext());

    expect(calls).toEqual(['discover']);
  });
});
