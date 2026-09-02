import type {
  ADMIN_ACTION_GATING_EFFECTS,
  ADMIN_PERMISSION_MATCH_STRATEGIES,
  ADMIN_PERMISSION_POLICY_SCHEMA_VERSION,
} from './admin-permissions.constants.js';

/**
 * @alpha
 * Versioned permission policy schema discriminator.
 */
export type AdminPermissionPolicySchemaVersionType =
  typeof ADMIN_PERMISSION_POLICY_SCHEMA_VERSION;

/**
 * @alpha
 * Admin role identifier used for role-to-permission mapping.
 */
export type AdminRoleIdentifierType = string;

/**
 * @alpha
 * Admin permission token used by policy gates.
 */
export type AdminPermissionTokenType = string;

/**
 * @alpha
 * Admin action identifier protected by policy gates.
 */
export type AdminActionIdentifierType = string;

/**
 * @alpha
 * Decision effect returned by an admin action gate.
 */
export type AdminActionGatingEffectType =
  (typeof ADMIN_ACTION_GATING_EFFECTS)[number];

/**
 * @alpha
 * Permission matching strategy for action gate requirements.
 */
export type AdminPermissionMatchStrategyType =
  (typeof ADMIN_PERMISSION_MATCH_STRATEGIES)[number];
