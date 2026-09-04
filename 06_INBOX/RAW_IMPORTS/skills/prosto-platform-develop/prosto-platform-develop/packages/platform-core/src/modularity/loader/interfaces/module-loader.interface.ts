import type { ModuleArtifactSourceDescriptorType } from './artifact-source.interface.js';
import type { IModuleCandidateArtifact } from './module-candidate-artifact.interface.js';
import type { IRejectedModuleArtifact } from './rejected-module-artifact.interface.js';

/**
 * @alpha
 * Result of module loading operation.
 */
export interface IModulesLoadResult {
  readonly loaded: readonly IModuleCandidateArtifact[];
  readonly rejected: readonly IRejectedModuleArtifact[];
}

/**
 * @alpha
 * Module loader contract.
 */
export interface IModuleLoader {
  load(
    sourceDescriptors: readonly ModuleArtifactSourceDescriptorType[],
  ): Promise<IModulesLoadResult>;
}
