/** @alpha */
export class AdminAuthClientError extends Error {
  constructor(
    readonly reason:
      | 'AUTHENTICATION_FAILED'
      | 'CSRF_UNAVAILABLE'
      | 'INVALID_RESPONSE',
  ) {
    super(
      reason === 'CSRF_UNAVAILABLE'
        ? 'Authentication security context is unavailable.'
        : 'Authentication request failed.',
    );
    this.name = 'AdminAuthClientError';
  }
}
