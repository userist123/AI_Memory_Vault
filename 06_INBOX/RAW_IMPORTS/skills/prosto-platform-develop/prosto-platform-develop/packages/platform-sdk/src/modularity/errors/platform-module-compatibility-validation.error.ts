import { PlatformSdkError } from '@/errors/index.js';

/**
 * @alpha
 * Compatibility fields validated against runtime versions.
 */
export type PlatformModuleCompatibilityFieldType = 'sdkVersion' | 'nodeVersion';

/**
 * @alpha
 * Compatibility issue code taxonomy.
 */
export type PlatformModuleCompatibilityIssueCodeType =
  | 'VERSION_RANGE_MISMATCH'
  | 'RUNTIME_VERSION_MISSING'
  | 'RUNTIME_VERSION_INVALID';

/**
 * @alpha
 * Structured compatibility mismatch detail.
 */
export interface IPlatformModuleCompatibilityValidationIssue {
  readonly field: PlatformModuleCompatibilityFieldType;
  readonly code: PlatformModuleCompatibilityIssueCodeType;
  readonly message: string;
  readonly expectedRange: string;
  readonly actualVersion?: string;
}

/**
 * @alpha
 * Compatibility validation failure with detailed mismatch metadata.
 */
export class PlatformModuleCompatibilityValidationError extends PlatformSdkError {
  constructor(
    readonly issues: readonly IPlatformModuleCompatibilityValidationIssue[],
  ) {
    super(
      'COMPATIBILITY_VALIDATION_FAILED',
      'Module compatibility validation failed.',
      { issues },
    );

    this.name = 'CompatibilityValidationError';
    this.issues = issues;
  }
}
