import type { IRuntimeFailureDiagnostic } from '@/diagnostics/index.js';
import type { IModuleEnvelope } from '@/modularity/index.js';
import type { BootstrapStage } from '../constants/index.js';
import type {
  IBootstrapStage,
  IBootstrapStageContext,
  IBootstrapStageOutcome,
} from '../interfaces/index.js';

/**
 * @alpha
 * Abstract base class for bootstrap pipeline stages.
 * Provides common functionality for stage execution and context management.
 */
export abstract class BootstrapBaseStage implements IBootstrapStage {
  /**
   * Unique name identifying this stage.
   */
  abstract readonly stageType: BootstrapStage;

  /**
   * Execute the stage and return updated context.
   * @param context - The current bootstrap context
   * @returns Updated context after stage execution
   */
  abstract execute(
    context: IBootstrapStageContext,
  ): Promise<IBootstrapStageContext>;

  /**
   * Helper method to add a stage outcome to the context.
   */
  protected addOutcome(
    context: IBootstrapStageContext,
    outcome: Omit<IBootstrapStageOutcome, 'stage'>,
  ): IBootstrapStageContext {
    context.stageOutcomes.push({
      ...outcome,
      stage: this.stageType,
    });

    return context;
  }

  /**
   * Helper method to add a failure diagnostic to the context.
   */
  protected addFailure(
    context: IBootstrapStageContext,
    diagnostic: Omit<IRuntimeFailureDiagnostic, 'phase'>,
  ): IBootstrapStageContext {
    context.failedDiagnostics.push({
      ...diagnostic,
      phase: this.stageType,
    });

    return context;
  }

  /**
   * Helper method to mark a module as skipped.
   */
  protected skipModule(
    context: IBootstrapStageContext,
    moduleId: string,
  ): IBootstrapStageContext {
    context.skippedModuleIds.add(moduleId);

    return context;
  }

  /**
   * Helper method to add a module to the validated modules list.
   */
  protected addValidatedModule(
    context: IBootstrapStageContext,
    moduleEnvelope: IModuleEnvelope,
  ): IBootstrapStageContext {
    context.validatedModules.push(moduleEnvelope);

    return context;
  }

  /**
   * Helper method to stop the pipeline execution.
   */
  protected stopPipeline(
    context: IBootstrapStageContext,
  ): IBootstrapStageContext {
    context.abort = true;

    return context;
  }

  /**
   * Helper method to check if persistence is enabled.
   */
  protected isPersistenceEnabled(context: IBootstrapStageContext): boolean {
    const typeorm = context.persistenceConfiguration.typeorm;

    return (
      typeof typeorm === 'object' &&
      typeorm !== null &&
      (typeorm as Record<string, unknown>).enabled === true
    );
  }
}
