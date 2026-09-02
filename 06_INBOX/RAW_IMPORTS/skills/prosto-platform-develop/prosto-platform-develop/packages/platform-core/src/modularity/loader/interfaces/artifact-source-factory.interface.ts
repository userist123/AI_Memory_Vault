import type {
  IArtifactSource,
  ModuleArtifactSourceDescriptorType,
} from './artifact-source.interface.js';

/**
 * @alpha
 * Factory interface for creating artifact sources.
 */
export interface IArtifactSourceFactory {
  create(descriptor: ModuleArtifactSourceDescriptorType): IArtifactSource;
}
