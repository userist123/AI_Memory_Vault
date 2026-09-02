import type { IPlatformModuleCompatibilityValidationIssue } from '../errors/index.js';
import type { IPlatformModuleManifest } from './platform-module-manifest.interfaces.js';

/**
 * @alpha
 * Runtime version context used for manifest compatibility checks.
 */
export interface IPlatformRuntimeVersionContext {
  readonly sdkVersion: string;
  readonly nodeVersion?: string;
}

/**
 * @alpha
 * Successful compatibility validation result.
 */
export interface IPlatformModuleCompatibilityValidationSuccess {
  readonly compatible: true;
  readonly issues: readonly [];
}

/**
 * @alpha
 * Failed compatibility validation result.
 */
export interface IPlatformModuleCompatibilityValidationFailure {
  readonly compatible: false;
  readonly issues: readonly IPlatformModuleCompatibilityValidationIssue[];
}

/**
 * @alpha
 * Compatibility validation result union.
 */
export type PlatformModuleCompatibilityValidationResultType =
  | IPlatformModuleCompatibilityValidationSuccess
  | IPlatformModuleCompatibilityValidationFailure;

/**
 * @alpha
 * Contract for runtime compatibility validation.
 */
export interface IPlatformModuleCompatibilityValidator {
  validate(
    manifest: IPlatformModuleManifest,
    runtime: IPlatformRuntimeVersionContext,
  ): PlatformModuleCompatibilityValidationResultType;

  assert(
    manifest: IPlatformModuleManifest,
    runtime: IPlatformRuntimeVersionContext,
  ): void;
}
