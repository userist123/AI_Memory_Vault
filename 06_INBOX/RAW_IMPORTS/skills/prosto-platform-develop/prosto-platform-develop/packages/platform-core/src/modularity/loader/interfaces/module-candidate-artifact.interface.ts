import type {
  ModuleArtifactPackaging,
  ModuleArtifactSource,
} from '../constants/index.js';
import type { IModuleEnvelope } from './module-envelope.interface.js';

/**
 * @alpha
 * Candidate module artifact after successful loading and validation.
 */
export interface IModuleCandidateArtifact {
  readonly moduleId: string;
  readonly moduleVersion: string;
  readonly moduleEnvelope: IModuleEnvelope;
  readonly orderingKey: string;
  readonly sourceType: `${ModuleArtifactSource}`;
  readonly sourceRef: string;
  readonly packaging: `${ModuleArtifactPackaging}`;
}
