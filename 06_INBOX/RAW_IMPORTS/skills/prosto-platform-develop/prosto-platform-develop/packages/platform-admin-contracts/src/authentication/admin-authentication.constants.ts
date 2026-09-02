/**
 * @alpha
 * Version identifier for the first admin authentication API payload schema.
 */
export const ADMIN_AUTHENTICATION_API_SCHEMA_VERSION =
  'admin-authentication-api.v1' as const;

/**
 * @alpha
 * Authentication providers selectable by the admin BFF.
 */
export const ADMIN_AUTHENTICATION_MODES = ['local', 'oidc'] as const;

/**
 * @alpha
 * Account-independent session states exposed to the admin shell.
 */
export const ADMIN_AUTHENTICATION_SESSION_STATES = [
  'anonymous',
  'authenticated',
  'password-change-required',
] as const;

/**
 * @alpha
 * JSON endpoints forming the first version of the admin authentication API.
 */
export const ADMIN_AUTHENTICATION_API_ROUTES = {
  SESSION: '/admin/api/v1/auth/session',
  LOGIN: '/admin/api/v1/auth/login',
  CHANGE_PASSWORD: '/admin/api/v1/auth/change-password',
  LOGOUT: '/admin/api/v1/auth/logout',
} as const;

/**
 * @alpha
 * Protocol-level failure categories that do not disclose account existence.
 */
export const ADMIN_AUTHENTICATION_FAILURE_CODES = [
  'AUTHENTICATION_FAILED',
  'AUTHENTICATION_REQUIRED',
  'AUTHENTICATION_UNAVAILABLE',
  'INVALID_AUTHENTICATION_REQUEST',
  'INVALID_AUTHENTICATION_ORIGIN',
  'INVALID_AUTHENTICATION_CSRF',
  'PASSWORD_CHANGE_REQUIRED',
] as const;

/**
 * @alpha
 * Minimum password length accepted by the version one wire format.
 * A host may enforce a stricter password policy.
 */
export const ADMIN_AUTHENTICATION_MINIMUM_PASSWORD_LENGTH = 8;

/**
 * @alpha
 * Maximum credential field length accepted before password hashing.
 */
export const ADMIN_AUTHENTICATION_MAXIMUM_PASSWORD_LENGTH = 1024;

/**
 * @alpha
 * Maximum username length accepted by the version one wire format.
 */
export const ADMIN_AUTHENTICATION_MAXIMUM_USERNAME_LENGTH = 255;
