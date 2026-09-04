/**
 * @alpha
 * Configuration error intentionally containing no secret or account details.
 */
export class PlatformLocalAuthConfigurationError extends Error {
  constructor() {
    super('Local authentication configuration is invalid.');
    this.name = 'PlatformLocalAuthConfigurationError';
  }
}
