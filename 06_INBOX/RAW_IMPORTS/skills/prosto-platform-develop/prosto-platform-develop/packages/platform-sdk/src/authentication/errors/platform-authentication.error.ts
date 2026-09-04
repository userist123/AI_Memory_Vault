import { PlatformSdkError } from '@/errors/index.js';

/**
 * @alpha
 * Stable authentication failure categories for transport and host mappings.
 */
export type PlatformAuthenticationErrorCodeType =
  | 'AUTHENTICATION_FAILED'
  | 'AUTHENTICATION_REQUIRED'
  | 'AUTHENTICATION_UNAVAILABLE'
  | 'INVALID_AUTHENTICATION_REQUEST'
  | 'INVALID_AUTHENTICATION_ORIGIN'
  | 'INVALID_AUTHENTICATION_CSRF'
  | 'PASSWORD_CHANGE_REQUIRED';

/**
 * @alpha
 * Framework-neutral authentication failure with a transport-safe category.
 * Implementations must not place credentials, cookies, tokens, or account data in details.
 */
export class PlatformAuthenticationError extends PlatformSdkError {
  constructor(
    code: PlatformAuthenticationErrorCodeType,
    message: string,
    details?: Readonly<Record<string, unknown>>,
  ) {
    super(code, message, details);
    this.name = 'PlatformAuthenticationError';
  }
}
