import type { IAdminUIPluginManifestValidationIssue } from './admin-ui-plugin-manifest.interfaces.js';

/**
 * @alpha
 * Validation error raised when an admin UI plugin manifest violates schema or semantic rules.
 */
export class AdminUIPluginManifestValidationError extends Error {
  readonly issues: readonly IAdminUIPluginManifestValidationIssue[];

  constructor(issues: readonly IAdminUIPluginManifestValidationIssue[]) {
    super('Admin UI plugin manifest validation failed.');
    this.name = 'AdminUIPluginManifestValidationError';
    this.issues = issues;
  }
}
