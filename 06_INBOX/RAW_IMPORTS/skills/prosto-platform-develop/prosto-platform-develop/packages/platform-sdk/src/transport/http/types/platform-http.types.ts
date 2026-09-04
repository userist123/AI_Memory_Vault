import type {
  ALLOWED_APPLICATION_HTTP_METHODS,
  ALLOWED_IDENTITY_AUTHENTICATION_TYPES,
} from '../constants/index.js';

/**
 * @alpha
 * Union of allowed application HTTP methods.
 */
export type PlatformHttpMethodType =
  (typeof ALLOWED_APPLICATION_HTTP_METHODS)[number];

/**
 * @alpha
 * Union of allowed identity authentication types.
 */
export type PlatformIdentityAuthenticationTypeType =
  (typeof ALLOWED_IDENTITY_AUTHENTICATION_TYPES)[number];
