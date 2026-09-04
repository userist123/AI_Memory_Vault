import type { IModuleValidationStrategy } from '@/modularity/index.js';
import { type IBootstrapStageContext } from '../interfaces/index.js';
import { BootstrapStage } from '../constants/index.js';
import { BootstrapBaseStage } from './bootstrap.base-stage.js';

/**
 * @alpha
 * Bootstrap stage that validates module manifests, integrity, and compatibility.
 */
export class ValidateStage extends BootstrapBaseStage {
  readonly stageType = BootstrapStage.Validate;

  constructor(
    private readonly _validationStrategies: IModuleValidationStrategy[],
  ) {
    super();
  }

  override async execute(
    context: IBootstrapStageContext,
  ): Promise<IBootstrapStageContext> {
    // Process pre-rejected artifacts
    for (const preRejectedArtifact of context.preRejectedArtifacts) {
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

    candidates: for (const artifact of context.candidates) {
      // Skip pre-rejected modules
      if (context.skippedModuleIds.has(artifact.moduleId)) {
        continue;
      }

      for (const validationStrategy of this._validationStrategies) {
        const validationResult = validationStrategy.validate({
          artifact,
          runtimeVersion: context.runtimeVersion,
        });

        if ('error' in validationResult) {
          this.skipModule(context, artifact.moduleId);
          this.addFailure(context, {
            ...validationResult.error,
            moduleId: artifact.moduleId,
          });

          continue candidates;
        }
      }

      this.addValidatedModule(context, artifact.moduleEnvelope);
    }

    const validateFailuresCount = context.failedDiagnostics.filter(
      (item) => item.phase === this.stageType,
    ).length;

    this.addOutcome(context, {
      ok: validateFailuresCount === 0,
      details: `${context.validatedModules.length}/${context.candidates.length} modules validated`,
    });

    return context;
  }
}
