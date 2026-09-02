/**
 * @alpha
 * Configuration failure codes for the bearer adapter.
 */
export type PlatformAuthConfigurationErrorCodeType =
  | 'INVALID_ISSUER'
  | 'INVALID_JWKS_URI'
  | 'INVALID_AUDIENCES'
  | 'INVALID_ALLOWED_ALGORITHMS'
  | 'INVALID_JWKS_DURATION'
  | 'INVALID_CLAIM_NAME';

/**
 * @alpha
 * Safe deterministic error raised for invalid bearer adapter configuration.
 */
export class PlatformAuthConfigurationError extends Error {
  readonly code: PlatformAuthConfigurationErrorCodeType;

  constructor(code: PlatformAuthConfigurationErrorCodeType) {
    super('Invalid OIDC bearer authentication configuration.');
    this.name = 'PlatformAuthConfigurationError';
    this.code = code;
  }
}
