import type { IAdminDiscoveryPayloadValidationIssue } from './admin-discovery.interfaces.js';

/**
 * @alpha
 * Validation error raised when an admin discovery payload violates schema or semantic rules.
 */
export class AdminDiscoveryPayloadValidationError extends Error {
  readonly issues: readonly IAdminDiscoveryPayloadValidationIssue[];

  constructor(issues: readonly IAdminDiscoveryPayloadValidationIssue[]) {
    super('Admin discovery payload validation failed.');
    this.name = 'AdminDiscoveryPayloadValidationError';
    this.issues = issues;
  }
}
