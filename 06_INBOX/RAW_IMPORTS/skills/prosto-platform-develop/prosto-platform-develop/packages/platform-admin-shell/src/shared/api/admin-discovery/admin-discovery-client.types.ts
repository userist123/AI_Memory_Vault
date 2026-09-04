import type {
  IAdminDiscoveryPayload,
  IAdminDiscoveryPayloadValidationIssue,
} from '@prosto/platform-admin-contracts';

/**
 * @alpha
 * Configuration for the admin discovery contract client.
 */
export interface IAdminDiscoveryClientConfig {
  /**
   * Same-origin base URL of the admin BFF adapter.
   */
  readonly baseUrl: string;

  /**
   * Optional request timeout in milliseconds. Defaults to 10_000.
   */
  readonly timeoutMs?: number;

  /**
   * Optional custom fetch implementation. Defaults to global `fetch`.
   */
  readonly fetch?: typeof fetch;
}

/**
 * @alpha
 * Diagnostics metadata returned alongside the discovery payload.
 */
export interface IAdminDiscoveryClientDiagnostics {
  readonly acceptedCount: number;
  readonly rejectedCount: number;
  readonly duration: number;
}

/**
 * @alpha
 * BFF response envelope for the discovery endpoint.
 */
export interface IAdminDiscoveryBffEnvelope {
  readonly correlationId: string;
  readonly data: IAdminDiscoveryPayload;
  readonly diagnostics: IAdminDiscoveryClientDiagnostics;
}

/**
 * @alpha
 * Successful discovery client result.
 */
export interface IAdminDiscoveryClientSuccess {
  readonly success: true;
  readonly payload: IAdminDiscoveryPayload;
  readonly correlationId: string;
  readonly diagnostics: IAdminDiscoveryClientDiagnostics;
}

/**
 * @alpha
 * Failed discovery client result due to payload validation.
 */
export interface IAdminDiscoveryClientValidationFailure {
  readonly success: false;
  readonly reason: 'VALIDATION_FAILED';
  readonly correlationId: string | undefined;
  readonly issues: readonly IAdminDiscoveryPayloadValidationIssue[];
}

/**
 * @alpha
 * Failed discovery client result due to network or HTTP error.
 */
export interface IAdminDiscoveryClientNetworkFailure {
  readonly success: false;
  readonly reason: 'NETWORK_ERROR' | 'HTTP_ERROR' | 'TIMEOUT';
  readonly message: string;
  readonly statusCode?: number;
}

/**
 * @alpha
 * Failed discovery result indicating that browser authentication is required.
 */
export interface IAdminDiscoveryClientUnauthenticatedFailure {
  readonly success: false;
  readonly reason: 'UNAUTHENTICATED';
  readonly message: string;
  readonly statusCode: 401;
}

/**
 * @alpha
 * Discriminated union of all discovery client results.
 */
export type AdminDiscoveryClientResultType =
  | IAdminDiscoveryClientSuccess
  | IAdminDiscoveryClientValidationFailure
  | IAdminDiscoveryClientNetworkFailure
  | IAdminDiscoveryClientUnauthenticatedFailure;
