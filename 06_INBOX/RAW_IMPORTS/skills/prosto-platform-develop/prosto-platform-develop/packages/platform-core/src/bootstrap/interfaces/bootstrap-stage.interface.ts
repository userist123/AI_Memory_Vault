import type { BootstrapStage } from '../constants/index.js';
import type { IBootstrapStageContext } from './bootstrap-stage-context.interface.js';

/**
 * @alpha
 * Interface for bootstrap pipeline stages.
 * Each stage processes a specific phase of the bootstrap process.
 */
export interface IBootstrapStage {
  /**
   * Unique name identifying this stage.
   */
  readonly stageType: BootstrapStage;

  /**
   * Execute the stage and return updated context.
   * @param context - The current bootstrap context
   * @returns Updated context after stage execution
   */
  execute(context: IBootstrapStageContext): Promise<IBootstrapStageContext>;
}
