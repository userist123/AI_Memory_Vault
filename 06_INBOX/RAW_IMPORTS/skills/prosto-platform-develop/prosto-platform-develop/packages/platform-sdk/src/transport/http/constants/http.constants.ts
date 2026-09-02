/**
 * @alpha
 * Allowed application HTTP methods.
 * `OPTIONS` is reserved for transport/CORS preflight and not part of the application handler contract.
 */
export const ALLOWED_APPLICATION_HTTP_METHODS = [
  'GET',
  'POST',
  'PUT',
  'PATCH',
  'DELETE',
  'HEAD',
] as const;

/**
 * @alpha
 * Platform response header names.
 */
export const PLATFORM_RESPONSE_HEADER_NAMES = {
  CORRELATION_ID: 'X-Correlation-Id',
} as const;

/**
 * @alpha
 * Allowed identity authentication types.
 */
export const ALLOWED_IDENTITY_AUTHENTICATION_TYPES = [
  'anonymous',
  'delegated',
] as const;
