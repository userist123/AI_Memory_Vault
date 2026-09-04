import type {
  IBootstrapContext,
  IBootstrapCoordinator,
  IBootstrapInput,
  IBootstrapPipeline,
  IBootstrapStageContext,
} from './interfaces/index.js';

/**
 * @alpha
 * Bootstrap coordinator that orchestrates the module bootstrap pipeline.
 */
export class BootstrapCoordinator implements IBootstrapCoordinator {
  constructor(private readonly _pipeline: IBootstrapPipeline) {}

  /**
   * Coordinate the bootstrap process for the given input.
   * @param input - Bootstrap coordinator input
   * @returns Bootstrap context with results
   */
  async coordinate(input: IBootstrapInput): Promise<IBootstrapContext> {
    const initialStageContext: IBootstrapStageContext = {
      abort: false,
      stageOutcomes: [],
      failedDiagnostics: [],
      preRejectedArtifacts: [],
      candidates: [],
      validatedModules: [],
      loadedModules: [],
      skippedModuleIds: new Set<string>(),
      policyMode: input.policyMode,
      correlationId: input.correlationId,
      startupStartedAt: input.startupStartedAt,
      runtimeVersion: input.runtimeVersion,
      moduleSources: input.modules,
      persistenceProvider: input.persistenceProvider,
      platformPersistenceDescriptor: input.platformPersistenceDescriptor,
      persistenceConfiguration: input.persistenceConfiguration ?? {
        typeorm: { enabled: false },
      },
      services: input.services,
    };

    const result = await this._pipeline.execute(initialStageContext);

    return {
      policyMode: input.policyMode,
      loadedModules: result.loadedModules,
      stageOutcomes: result.stageOutcomes,
      failedDiagnostics: result.failedDiagnostics,
      skippedModuleIds: [...result.skippedModuleIds].sort((left, right) =>
        left.localeCompare(right),
      ),
    };
  }
}
