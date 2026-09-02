import type { IPlatformRuntimeVersionContext } from '@prosto/platform-sdk';
import type { IRuntimeFailureDiagnostic } from '@/diagnostics/index.js';
import type { IModuleCandidateArtifact } from '../../loader/index.js';

/**
 * @alpha
 * Input type for validation strategies.
 */
export interface IModuleValidationStrategyInput {
  artifact: IModuleCandidateArtifact;
  runtimeVersion: IPlatformRuntimeVersionContext;
}

export type ModuleValidationErrorType = Pick<
  IRuntimeFailureDiagnostic,
  'errorCode' | 'message' | 'remediationHint'
>;

/**
 * @alpha
 * Validation result type.
 */
export type ModuleValidationResultType =
  | { ok: true }
  | { ok: false; error: ModuleValidationErrorType };

/**
 * @alpha
 * Validation strategy contract for pluggable validation.
 */
export interface IModuleValidationStrategy {
  validate(input: IModuleValidationStrategyInput): ModuleValidationResultType;
}
