import type { IAdminAuthenticationValidationIssue } from './admin-authentication.interfaces.js';

/**
 * @alpha
 * Validation error raised when an admin authentication API payload is malformed.
 */
export class AdminAuthenticationValidationError extends Error {
  readonly issues: readonly IAdminAuthenticationValidationIssue[];

  constructor(issues: readonly IAdminAuthenticationValidationIssue[]) {
    super('Admin authentication payload validation failed.');
    this.name = 'AdminAuthenticationValidationError';
    this.issues = issues;
  }
}
