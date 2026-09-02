import type { IPlatformModuleManifest } from '@prosto/platform-sdk';
import {
  type IModuleLifecycleOrchestrator,
  isModuleCritical,
  type IStartupPolicyEvaluator,
} from '@/modularity/index.js';
import type { IBootstrapStageContext } from '../interfaces/index.js';
import { BootstrapStage } from '../constants/index.js';
import { BootstrapBaseStage } from './bootstrap.base-stage.js';

/** @alpha Executes module start hooks after persistence readiness. */
export class ModulesStartStage extends BootstrapBaseStage {
  readonly stageType = BootstrapStage.Start;

  constructor(
    private readonly _startupPolicyEvaluator: IStartupPolicyEvaluator,
    private readonly _moduleLifecycleOrchestrator: IModuleLifecycleOrchestrator,
  ) {
    super();
  }

  override async execute(
    context: IBootstrapStageContext,
  ): Promise<IBootstrapStageContext> {
    const result = await this._moduleLifecycleOrchestrator.startModules(
      context.loadedModules,
      {
        startupPolicy: context.policyMode,
        sdkVersion: context.runtimeVersion.sdkVersion,
        persistenceProvider: context.persistenceProvider,
        persistenceEnabled: this.isPersistenceEnabled(context),
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

        // Shutdown all started modules
        await this._moduleLifecycleOrchestrator.stopModules(
          result.startedModules,
          {
            startupPolicy: context.policyMode,
            sdkVersion: context.runtimeVersion.sdkVersion,
            timeoutMs: 1000 * 60, // 1 minute
          },
        );

        return { ...context, loadedModules: [] };
      }
    }

    this.addOutcome(context, { ok: true, details: 'Module start completed.' });

    return { ...context, loadedModules: [...result.startedModules] };
  }
}
