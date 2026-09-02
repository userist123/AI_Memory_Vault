import type {
  IArtifactSourceFactory,
  IModuleCandidateArtifact,
  IModuleLoader,
  IModulesLoadResult,
  IRejectedModuleArtifact,
  ModuleArtifactSourceDescriptorType,
} from './interfaces/index.js';
import { ArtifactSourceFactory } from './factories/index.js';

/**
 * @alpha
 * Module loader that delegates source-specific loading to IArtifactSource instances.
 */
export class ModuleLoader implements IModuleLoader {
  constructor(
    private readonly _artifactSourceFactory: IArtifactSourceFactory = new ArtifactSourceFactory(),
  ) {}

  async load(
    sourceDescriptors: readonly ModuleArtifactSourceDescriptorType[],
  ): Promise<IModulesLoadResult> {
    const loaded: IModuleCandidateArtifact[] = [];
    const rejected: IRejectedModuleArtifact[] = [];

    for (const sourceDescriptor of sourceDescriptors) {
      const source = this._artifactSourceFactory.create(sourceDescriptor);
      const result = await source.load();

      if (this._isRejectedArtifact(result)) {
        rejected.push(result);
      } else {
        loaded.push(result);
      }
    }

    loaded.sort((left, right) =>
      left.orderingKey.localeCompare(right.orderingKey),
    );

    rejected.sort((left, right) => {
      const byModule = left.moduleId.localeCompare(right.moduleId);

      return byModule !== 0
        ? byModule
        : left.sourceRef.localeCompare(right.sourceRef);
    });

    return { loaded, rejected };
  }

  protected _isRejectedArtifact(
    artifact: IModuleCandidateArtifact | IRejectedModuleArtifact,
  ): artifact is IRejectedModuleArtifact {
    return 'reasonCode' in artifact;
  }
}
