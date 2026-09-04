import {
  DependencyCycleError,
  DependencyGraph,
  type IModuleEnvelope,
  isModuleCritical,
  type IStartupPolicyEvaluator,
  TopologicalSorter,
} from '@/modularity/index.js';
import type { IBootstrapStageContext } from '../interfaces/index.js';
import { RuntimeErrorCodes } from '@/common/index.js';
import { BootstrapStage } from '../constants/index.js';
import { BootstrapBaseStage } from './bootstrap.base-stage.js';

/**
 * @alpha
 * Bootstrap stage that resolves module dependencies and performs topological sorting.
 */
export class ResolveDependenciesStage extends BootstrapBaseStage {
  readonly stageType = BootstrapStage.Resolve;

  constructor(
    private readonly _startupPolicyEvaluator: IStartupPolicyEvaluator,
  ) {
    super();
  }

  override async execute(
    context: IBootstrapStageContext,
  ): Promise<IBootstrapStageContext> {
    const validatedModules = context.validatedModules;

    if (!validatedModules.length) {
      this.addOutcome(context, { ok: false, details: 'No validated modules' });
      return context;
    }

    const dependencyGraph = DependencyGraph.create(validatedModules);
    const topologicalSorter = TopologicalSorter.create();

    let orderedModules: IModuleEnvelope[] = [];

    try {
      const topologicalSortResult = topologicalSorter.sort(dependencyGraph);

      // Check for missing dependencies
      for (const [
        moduleId,
        missing,
      ] of topologicalSortResult.missingDependencies) {
        this.skipModule(context, moduleId);
        this.addFailure(context, {
          moduleId,
          errorCode: RuntimeErrorCodes.DependencyMissing,
          message: `Missing required dependencies: ${missing.join(', ')}`,
          remediationHint:
            'Ensure all required dependencies are discoverable by runtime.',
        });

        const moduleEnvelope = dependencyGraph.getModule(moduleId);
        const policy = this._startupPolicyEvaluator.evaluate({
          moduleId,
          policyMode: context.policyMode,
          critical:
            !moduleEnvelope || isModuleCritical(moduleEnvelope.manifest),
        });

        if (policy.action === 'abort') {
          this.addOutcome(context, { ok: false, details: policy.reason });
          this.stopPipeline(context);

          return { ...context, loadedModules: [] };
        }
      }

      orderedModules = topologicalSortResult.orderedModules.filter(
        (module) => !context.skippedModuleIds.has(module.manifest.id),
      );

      this.addOutcome(context, { ok: true });
    } catch (error) {
      if (error instanceof DependencyCycleError) {
        this.addFailure(context, {
          moduleId: error.moduleIds.join(', '),
          errorCode: RuntimeErrorCodes.DependencyCycleDetected,
          message: error.message,
          remediationHint: 'Remove dependency cycle between impacted modules.',
        });
      } else {
        this.addFailure(context, {
          moduleId: 'unknown',
          errorCode: RuntimeErrorCodes.DependencyFailed,
          message:
            error instanceof Error
              ? error.message
              : 'Unknown graph resolution error.',
          remediationHint: 'Inspect dependency graph resolver inputs.',
        });
      }

      this.addOutcome(context, {
        ok: false,
        details: 'Dependency graph resolution failed',
      });

      this.stopPipeline(context);

      // return { ...context, loadedModules: [] };
    }

    return { ...context, loadedModules: orderedModules };
  }
}
