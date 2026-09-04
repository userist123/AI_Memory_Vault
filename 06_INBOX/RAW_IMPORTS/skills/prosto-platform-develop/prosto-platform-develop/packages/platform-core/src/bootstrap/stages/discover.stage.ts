import type { IModuleLoader } from '@/modularity/index.js';
import type { IBootstrapStageContext } from '../interfaces/index.js';
import { BootstrapStage } from '../constants/index.js';
import { BootstrapBaseStage } from './bootstrap.base-stage.js';

/**
 * @alpha
 * Bootstrap stage responsible for loading modules and processing pre-rejected artifacts.
 *
 * Converts raw source descriptors into discovered module artifacts
 * (delegating semantic validation and content loading to IArtifactSource instances via ModuleLoader).
 */
export class DiscoverStage extends BootstrapBaseStage {
  readonly stageType = BootstrapStage.Discover;

  constructor(private readonly _moduleLoader: IModuleLoader) {
    super();
  }

  override async execute(
    context: IBootstrapStageContext,
  ): Promise<IBootstrapStageContext> {
    const modulesLoadResult = await this._moduleLoader.load(
      context.moduleSources,
    );

    // Process pre-rejected artifacts
    for (const preRejectedArtifact of modulesLoadResult.rejected) {
      if (preRejectedArtifact.phase !== this.stageType) {
        continue;
      }

      this.skipModule(context, preRejectedArtifact.moduleId);
      this.addFailure(context, {
        moduleId: preRejectedArtifact.moduleId,
        errorCode: preRejectedArtifact.reasonCode,
        message: preRejectedArtifact.message,
        remediationHint: preRejectedArtifact.remediationHint,
      });
    }

    const discoveryFailuresCount = context.failedDiagnostics.filter(
      (item) => item.phase === this.stageType,
    ).length;

    this.addOutcome(context, {
      ok: discoveryFailuresCount === 0,
      details:
        discoveryFailuresCount > 0
          ? `${discoveryFailuresCount} modules rejected during discover stage`
          : undefined,
    });

    return {
      ...context,
      preRejectedArtifacts: modulesLoadResult.rejected,
      candidates: modulesLoadResult.loaded,
    };
  }
}
