import type { IPlatformModuleManifest } from '@prosto/platform-sdk';
import { RuntimeErrorCodes } from '@/common/index.js';
import {
  type IModuleLifecycleOrchestrator,
  isModuleCritical,
  type IStartupPolicyEvaluator,
} from '@/modularity/index.js';
import type { IBootstrapStageContext } from '../interfaces/index.js';
import { BootstrapStage } from '../constants/index.js';
import { BootstrapBaseStage } from './bootstrap.base-stage.js';

/**
 * @alpha
 * Executes module init hooks before the persistence barrier.
 */
export class ModulesInitializationStage extends BootstrapBaseStage {
  readonly stageType = BootstrapStage.Initialize;

  constructor(
    private readonly _startupPolicyEvaluator: IStartupPolicyEvaluator,
    private readonly _moduleLifecycleOrchestrator: IModuleLifecycleOrchestrator,
  ) {
    super();
  }

  override async execute(
    context: IBootstrapStageContext,
  ): Promise<IBootstrapStageContext> {
    const persistenceEnabled = this.isPersistenceEnabled(context);

    if (persistenceEnabled && !context.persistenceProvider) {
      this._addPersistenceFailure(
        context,
        'Persistence provider is required when TypeORM persistence is enabled.',
        'Configure an SDK-compatible persistence provider in RuntimeBuilder options.',
      );
      this.stopPipeline(context);

      return context;
    }

    if (persistenceEnabled && !!context.platformPersistenceDescriptor) {
      try {
        context.persistenceProvider?.descriptors.register(
          'platform',
          context.platformPersistenceDescriptor,
        );
      } catch (error) {
        this._addPersistenceFailure(
          context,
          'Platform persistence descriptor registration failed.',
          this._getRemediationHint(error),
        );
        this.stopPipeline(context);

        return context;
      }
    }

    const result = await this._moduleLifecycleOrchestrator.initializeModules(
      context.loadedModules,
      {
        persistenceEnabled,
        persistenceProvider: context.persistenceProvider,
        startupPolicy: context.policyMode,
        sdkVersion: context.runtimeVersion.sdkVersion,
      },
    );

    const manifests = new Map<string, IPlatformModuleManifest>(
      context.loadedModules.map((moduleEnvelope) => [
        moduleEnvelope.manifest.id,
        moduleEnvelope.manifest,
      ]),
    );

    for (const issue of result.issues) {
      this.skipModule(context, issue.moduleId);
      this.addFailure(context, issue);

      const manifest = manifests.get(issue.moduleId);
      const policy = this._startupPolicyEvaluator.evaluate({
        moduleId: issue.moduleId,
        policyMode: context.policyMode,
        critical: !manifest || isModuleCritical(manifest),
      });

      if (policy.action === 'abort') {
        this.addOutcome(context, { ok: false, details: policy.reason });
        this.stopPipeline(context);

        return { ...context, loadedModules: [] };
      }
    }

    this.addOutcome(context, {
      ok: true,
      details: 'Module initialization completed.',
    });
    return { ...context, loadedModules: [...result.initializedModules] };
  }

  private _addPersistenceFailure(
    context: IBootstrapStageContext,
    message: string,
    remediationHint: string,
  ): void {
    this.addFailure(context, {
      moduleId: 'platform',
      errorCode: RuntimeErrorCodes.PersistenceFailed,
      message,
      remediationHint,
    });
    this.addOutcome(context, { ok: false, details: message });
  }

  private _getRemediationHint(error: unknown): string {
    if (
      typeof error === 'object' &&
      error !== null &&
      'details' in error &&
      typeof error.details === 'object' &&
      error.details !== null &&
      typeof (error.details as Record<string, unknown>).remediationHint ===
        'string'
    ) {
      return (
        (error.details as Record<string, string>).remediationHint ??
        'Correct the platform persistence descriptor and retry startup.'
      );
    }

    return 'Correct the platform persistence descriptor and retry startup.';
  }
}
