import type {
  ArtifactSourceValidationResultType,
  IModuleCandidateArtifact,
  IModuleMemoryArtifactSource,
  IRejectedModuleArtifact,
} from '../interfaces/index.js';
import { RuntimeErrorCodes } from '@/common/index.js';
import {
  ModuleArtifactPackaging,
  ModuleArtifactSource,
  ModuleState,
} from '../constants/index.js';
import { ArtifactBaseSource } from './artifact.base-source.js';

/**
 * @alpha
 * Memory-based artifact source for in-memory module instances.
 */
export class MemorySource extends ArtifactBaseSource {
  constructor(private readonly _descriptor: IModuleMemoryArtifactSource) {
    super(ModuleArtifactSource.Memory);
  }

  /**
   * Memory sources are always valid if a module instance exists.
   */
  override validate(): ArtifactSourceValidationResultType {
    if (!this._descriptor.module) {
      return {
        ok: false,
        error: {
          reasonCode: RuntimeErrorCodes.SourceEntryResolveFailed,
          message: 'Memory source artifact does not contain module instance.',
          remediationHint: 'Provide module object for memory source.',
        },
      };
    }

    return { ok: true };
  }

  /**
   * Load module from memory - returns immediately with normalized artifact.
   */
  override async load(): Promise<
    IModuleCandidateArtifact | IRejectedModuleArtifact
  > {
    const validation = this.validate();

    if (!validation.ok) {
      return this.createRejected('discover', validation.error);
    }

    const { module, manifest } = this._descriptor;
    const fullPath = this.getSourceRef();

    const candidateArtifact: IModuleCandidateArtifact = {
      moduleId: manifest.id,
      moduleVersion: manifest.version,
      moduleEnvelope: {
        module,
        manifest,
        fullPhysicalPath: fullPath,
        state: ModuleState.ReadyForInitialization,
      },
      orderingKey: fullPath,
      sourceType: ModuleArtifactSource.Memory,
      sourceRef: fullPath,
      packaging: ModuleArtifactPackaging.Esm,
    };

    return candidateArtifact;
  }

  protected override getModuleIdHint(): string | undefined {
    return this._descriptor.manifest.id ?? this._descriptor.moduleIdHint;
  }

  protected override getSourceRef(): string {
    const manifest = this._descriptor.manifest;
    return `memory:${manifest.id}@${manifest.version}`;
  }
}
