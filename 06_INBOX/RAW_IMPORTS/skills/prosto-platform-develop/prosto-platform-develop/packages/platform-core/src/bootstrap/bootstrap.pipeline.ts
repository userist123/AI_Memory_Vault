import type {
  IBootstrapPipeline,
  IBootstrapStage,
  IBootstrapStageContext,
} from './interfaces/index.js';

/**
 * @alpha
 * Bootstrap pipeline that executes stages in sequence.
 */
export class BootstrapPipeline implements IBootstrapPipeline {
  constructor(private readonly _stages: readonly IBootstrapStage[]) {}

  /**
   * Creates a BootstrapPipeline from an array of stages.
   * @param stages - Array of bootstrap stages
   * @returns New BootstrapPipeline instance
   */
  static create(stages: readonly IBootstrapStage[]): BootstrapPipeline {
    return new BootstrapPipeline(stages);
  }

  /**
   * Execute all stages in the pipeline.
   * @param initialContext - Initial bootstrap context
   * @returns Final bootstrap context after all stages
   */
  async execute(
    initialContext: IBootstrapStageContext,
  ): Promise<IBootstrapStageContext> {
    let context = initialContext;

    for (const stage of this._stages) {
      context = await stage.execute(context);

      if (context.abort) {
        break;
      }
    }

    return context;
  }
}
