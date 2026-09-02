import type { IPlatformModule } from '@prosto/platform-sdk';
import type {
  ModuleArtifactPackaging,
  ModuleArtifactSource,
} from '../constants/index.js';
import type { IModuleArtifactIntegrity } from './module-artifact-integrity.interface.js';

/**
 * @alpha
 * Discovered module artifact during the discovery phase.
 */
export interface IDiscoveredModuleArtifact {
  readonly sourceType: `${ModuleArtifactSource}`;
  readonly sourceRef: string;
  readonly packaging: `${ModuleArtifactPackaging}`;
  readonly orderingKey: string;
  readonly moduleIdHint?: string;
  readonly moduleVersionHint?: string;
  readonly module?: IPlatformModule;
  readonly integrity?: IModuleArtifactIntegrity;
}
