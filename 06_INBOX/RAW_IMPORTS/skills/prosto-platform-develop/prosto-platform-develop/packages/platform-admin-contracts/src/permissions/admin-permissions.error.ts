import type { IAdminPermissionPolicyValidationIssue } from './admin-permissions.interfaces.js';

/**
 * @alpha
 * Error raised when admin permission policy validation fails.
 */
export class AdminPermissionPolicyValidationError extends Error {
  readonly issues: readonly IAdminPermissionPolicyValidationIssue[];

  constructor(issues: readonly IAdminPermissionPolicyValidationIssue[]) {
    super('Admin permission policy validation failed.');
    this.name = 'AdminPermissionPolicyValidationError';
    this.issues = issues;
  }
}
