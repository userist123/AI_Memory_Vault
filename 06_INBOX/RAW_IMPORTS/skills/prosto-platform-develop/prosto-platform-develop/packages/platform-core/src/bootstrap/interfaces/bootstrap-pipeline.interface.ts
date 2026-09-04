import type { IBootstrapStageContext } from './bootstrap-stage-context.interface.js';

/**
 * @alpha
 * Bootstrap pipeline contract.
 */
export interface IBootstrapPipeline {
  execute(context: IBootstrapStageContext): Promise<IBootstrapStageContext>;
}
