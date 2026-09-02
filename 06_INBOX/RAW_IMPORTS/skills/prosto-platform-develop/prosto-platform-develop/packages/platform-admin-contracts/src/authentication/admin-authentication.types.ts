import type {
  ADMIN_AUTHENTICATION_API_SCHEMA_VERSION,
  ADMIN_AUTHENTICATION_FAILURE_CODES,
  ADMIN_AUTHENTICATION_MODES,
  ADMIN_AUTHENTICATION_SESSION_STATES,
} from './admin-authentication.constants.js';

/**
 * @alpha
 * Versioned schema discriminator for admin authentication API payloads.
 */
export type AdminAuthenticationApiSchemaVersionType =
  typeof ADMIN_AUTHENTICATION_API_SCHEMA_VERSION;

/**
 * @alpha
 * Authentication provider mode exposed to the admin shell.
 */
export type AdminAuthenticationModeType =
  (typeof ADMIN_AUTHENTICATION_MODES)[number];

/**
 * @alpha
 * Account-independent session state exposed to the admin shell.
 */
export type AdminAuthenticationSessionStateType =
  (typeof ADMIN_AUTHENTICATION_SESSION_STATES)[number];

/**
 * @alpha
 * Non-enumerating authentication failure category returned by the admin BFF.
 */
export type AdminAuthenticationFailureCodeType =
  (typeof ADMIN_AUTHENTICATION_FAILURE_CODES)[number];
