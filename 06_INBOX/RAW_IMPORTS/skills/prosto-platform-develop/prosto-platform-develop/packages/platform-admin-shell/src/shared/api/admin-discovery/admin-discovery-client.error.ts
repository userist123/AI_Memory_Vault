import type { IAdminDiscoveryPayloadValidationIssue } from '@prosto/platform-admin-contracts';

/**
 * Error raised when the admin discovery contract client encounters
 * an authentication, network, HTTP, timeout, or payload validation failure.
 */
export class AdminDiscoveryClientError extends Error {
  readonly statusCode?: number;
  readonly issues: readonly IAdminDiscoveryPayloadValidationIssue[];

  constructor(
    readonly reason:
      | 'NETWORK_ERROR'
      | 'HTTP_ERROR'
      | 'TIMEOUT'
      | 'UNAUTHENTICATED'
      | 'VALIDATION_FAILED',
    message: string,
    options?: {
      statusCode?: number;
      issues?: readonly IAdminDiscoveryPayloadValidationIssue[];
      cause?: unknown;
    },
  ) {
    super(message);

    this.name = 'AdminDiscoveryClientError';
    this.statusCode = options?.statusCode;
    this.issues = options?.issues ?? [];

    if (options?.cause !== undefined) {
      (this as { cause: unknown }).cause = options.cause;
    }
  }
}
