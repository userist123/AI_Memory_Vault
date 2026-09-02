import { PlatformSdkError } from '@/errors/index.js';

/**
 * @alpha
 * Structured issue captured during manifest validation.
 */
export interface IPlatformModuleManifestValidationIssue {
  readonly code: string;
  readonly message: string;
  readonly path: string;
}

/**
 * @alpha
 * Manifest validation failure with machine-readable issue details.
 */
export class PlatformModuleManifestValidationError extends PlatformSdkError {
  constructor(
    readonly issues: Readonly<IPlatformModuleManifestValidationIssue>[],
  ) {
    super(
      'MANIFEST_VALIDATION_FAILED',
      'Platform module manifest validation failed.',
      { issues },
    );

    this.name = 'ManifestValidationError';
    this.issues = issues;
  }
}
