import type { PlatformModuleManifestValidationError } from '../errors/index.js';
import type { IPlatformModuleManifest } from './platform-module-manifest.interfaces.js';

/**
 * @alpha
 * Successful manifest validation result.
 */
export interface IPlatformModuleManifestValidationSuccess {
  readonly success: true;
  readonly manifest: IPlatformModuleManifest;
}

/**
 * @alpha
 * Failed manifest validation result.
 */
export interface IPlatformModuleManifestValidationFailure {
  readonly success: false;
  readonly error: PlatformModuleManifestValidationError;
}

/**
 * @alpha
 * Discriminated union for manifest validation outcomes.
 */
export type PlatformModuleManifestValidationResultType =
  | IPlatformModuleManifestValidationSuccess
  | IPlatformModuleManifestValidationFailure;

/**
 * @alpha
 * Contract for manifest validation operations.
 */
export interface IPlatformModuleManifestValidator {
  validate(manifest: unknown): PlatformModuleManifestValidationResultType;
  parse(manifest: unknown): IPlatformModuleManifest;
}
