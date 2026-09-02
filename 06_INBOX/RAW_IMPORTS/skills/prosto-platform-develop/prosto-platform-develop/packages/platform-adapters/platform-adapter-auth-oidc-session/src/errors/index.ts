/** @alpha */
export type PlatformOidcSessionConfigurationErrorCodeType =
  'INVALID_OIDC_SESSION_CONFIGURATION';

/** @alpha */
export class PlatformOidcSessionConfigurationError extends Error {
  readonly code: PlatformOidcSessionConfigurationErrorCodeType =
    'INVALID_OIDC_SESSION_CONFIGURATION';

  constructor() {
    super('OIDC session configuration is invalid.');
    this.name = 'PlatformOidcSessionConfigurationError';
  }
}
