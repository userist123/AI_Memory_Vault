/**
 * @alpha
 * Authentication provider modes supported by host-level authentication facades.
 */
export const PLATFORM_AUTHENTICATION_PROVIDER_MODES = [
  'local',
  'oidc',
] as const;

/**
 * @alpha
 * Authentication states that a provider can expose without disclosing subject data.
 */
export const PLATFORM_AUTHENTICATION_SESSION_STATES = [
  'anonymous',
  'authenticated',
  'password-change-required',
] as const;
