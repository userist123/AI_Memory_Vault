import type {
  IPlatformModule,
  IPlatformModuleManifest,
} from '@prosto/platform-sdk';
import type { RuntimeErrorCodes } from '@/common/index.js';
import type {
  ModuleArtifactPackaging,
  ModuleArtifactSource,
} from '../constants/index.js';
import type { IModuleArtifactIntegrity } from './module-artifact-integrity.interface.js';
import type { IModuleCandidateArtifact } from './module-candidate-artifact.interface.js';
import type { IRejectedModuleArtifact } from './rejected-module-artifact.interface.js';

/**
 * @alpha
 * Artifact source for loading module from memory.
 */
export interface IModuleMemoryArtifactSource {
  readonly type: 'memory';
  readonly module: IPlatformModule;
  readonly manifest: IPlatformModuleManifest;
  readonly moduleIdHint?: string;
}

/**
 * @alpha
 * Artifact source for loading module from a local path.
 */
export interface IModulePathArtifactSource {
  readonly type: 'path';
  readonly path: string;
  readonly packaging?: `${ModuleArtifactPackaging}`;
  readonly integrity?: IModuleArtifactIntegrity;
  readonly moduleIdHint?: string;
}

/**
 * @alpha
 * Artifact source for loading module from a remote URL.
 */
export interface IModuleUrlArtifactSource {
  readonly type: 'url';
  readonly url: string;
  readonly packaging?: `${ModuleArtifactPackaging}`;
  readonly integrity?: IModuleArtifactIntegrity;
  readonly moduleIdHint?: string;
}

/**
 * @alpha
 * Artifact source for loading module from a package registry.
 */
export interface IModuleRegistryArtifactSource {
  readonly type: 'registry';
  readonly packageName: string;
  readonly version: string;
  readonly registryUrl?: string;
  readonly authToken?: string;
  readonly authType?: 'bearer' | 'basic';
  readonly packaging?: `${ModuleArtifactPackaging}`;
  readonly integrity?: IModuleArtifactIntegrity;
  readonly moduleIdHint?: string;
}

/**
 * @alpha
 * Union of all supported artifact source types.
 */
export type ModuleArtifactSourceDescriptorType =
  | IModuleMemoryArtifactSource
  | IModulePathArtifactSource
  | IModuleUrlArtifactSource
  | IModuleRegistryArtifactSource;

/**
 * @alpha
 * Validation result for an artifact source.
 */
export type ArtifactSourceValidationResultType =
  | { ok: true }
  | {
      ok: false;
      error: {
        reasonCode: RuntimeErrorCodes;
        message: string;
        remediationHint: string;
      };
    };

/**
 * @alpha
 * Artifact source contract for loading module artifacts.
 */
export interface IArtifactSource {
  readonly type: ModuleArtifactSource;

  /**
   * Validate source configuration before loading.
   */
  validate(): ArtifactSourceValidationResultType;

  /**
   * Load module artifact from the source.
   */
  load(): Promise<IModuleCandidateArtifact | IRejectedModuleArtifact>;
}
