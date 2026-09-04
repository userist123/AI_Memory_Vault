import type { RuntimeErrorCodes, RuntimeStage } from '@/common/index.js';
import type { ModuleArtifactSource } from '../constants/index.js';

/**
 * @alpha
 * Rejected module artifact during the discovery or validation phase.
 */
export interface IRejectedModuleArtifact {
  readonly moduleId: string;
  readonly sourceType: `${ModuleArtifactSource}`;
  readonly sourceRef: string;
  readonly phase: `${RuntimeStage.Discover}` | `${RuntimeStage.Validate}`;
  readonly reasonCode: `${RuntimeErrorCodes}`;
  readonly message: string;
  readonly remediationHint: string;
}
