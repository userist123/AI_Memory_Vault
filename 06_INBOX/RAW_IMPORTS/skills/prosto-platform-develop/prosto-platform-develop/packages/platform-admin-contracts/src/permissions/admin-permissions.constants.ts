/**
 * @alpha
 * Version identifier for the first admin permission policy schema.
 */
export const ADMIN_PERMISSION_POLICY_SCHEMA_VERSION =
  'admin-permission-policy.v1' as const;

/**
 * @alpha
 * Regex pattern for admin role identifiers.
 */
export const ADMIN_ROLE_ID_PATTERN = /^[a-z][a-z0-9-]*(?:\.[a-z][a-z0-9-_]*)*$/;

/**
 * @alpha
 * Regex pattern for admin permission tokens used by role mappings.
 */
export const ADMIN_PERMISSION_TOKEN_PATTERN =
  /^[a-z][a-z0-9-]*(?:\.[a-z][a-z0-9-_]*)*(?::[a-z][a-z0-9-_]*)?$/;

/**
 * @alpha
 * Regex pattern for gated admin action identifiers.
 */
export const ADMIN_ACTION_ID_PATTERN =
  /^[a-z][a-z0-9-]*(?:\.[a-z][a-z0-9-_]*)*$/;

/**
 * @alpha
 * Supported action gating effects for admin policy decisions.
 */
export const ADMIN_ACTION_GATING_EFFECTS = ['allow', 'deny'] as const;

/**
 * @alpha
 * Supported requirement matching strategies for admin action gates.
 */
export const ADMIN_PERMISSION_MATCH_STRATEGIES = ['all', 'any'] as const;
