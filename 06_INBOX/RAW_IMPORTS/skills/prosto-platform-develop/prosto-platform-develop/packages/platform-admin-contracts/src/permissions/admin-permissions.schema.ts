import { z } from 'zod';
import {
  ADMIN_ACTION_GATING_EFFECTS,
  ADMIN_ACTION_ID_PATTERN,
  ADMIN_PERMISSION_MATCH_STRATEGIES,
  ADMIN_PERMISSION_POLICY_SCHEMA_VERSION,
  ADMIN_PERMISSION_TOKEN_PATTERN,
  ADMIN_ROLE_ID_PATTERN,
} from './admin-permissions.constants.js';

/**
 * @alpha
 * Zod schema for admin role identifiers.
 */
export const AdminRoleIdentifierSchema = z
  .string()
  .regex(ADMIN_ROLE_ID_PATTERN, {
    message: 'Role id must use dot-separated lowercase segments.',
  });

/**
 * @alpha
 * Zod schema for admin permission tokens.
 */
export const AdminPermissionTokenSchema = z
  .string()
  .regex(ADMIN_PERMISSION_TOKEN_PATTERN, {
    message:
      'Permission must use dot-separated lowercase segments with an optional action suffix.',
  });

/**
 * @alpha
 * Zod schema for gated admin action identifiers.
 */
export const AdminActionIdentifierSchema = z
  .string()
  .regex(ADMIN_ACTION_ID_PATTERN, {
    message: 'Action id must use dot-separated lowercase segments.',
  });

/**
 * @alpha
 * Zod schema for role-to-permission mappings.
 */
export const AdminRolePermissionMappingSchema = z
  .object({
    roleId: AdminRoleIdentifierSchema,
    permissions: z.array(AdminPermissionTokenSchema),
    description: z.string().min(1).optional(),
  })
  .strict();

/**
 * @alpha
 * Zod schema for admin action gate policies.
 */
export const AdminActionGatePolicySchema = z
  .object({
    actionId: AdminActionIdentifierSchema,
    requiredPermissions: z.array(AdminPermissionTokenSchema),
    match: z.enum(ADMIN_PERMISSION_MATCH_STRATEGIES),
    effect: z.enum(ADMIN_ACTION_GATING_EFFECTS),
    remediationHint: z.string().min(1).optional(),
  })
  .strict();

/**
 * @alpha
 * Zod schema for versioned admin permission policies.
 */
export const AdminPermissionPolicySchema = z
  .object({
    schemaVersion: z.literal(ADMIN_PERMISSION_POLICY_SCHEMA_VERSION),
    roleMappings: z.array(AdminRolePermissionMappingSchema),
    actionGates: z.array(AdminActionGatePolicySchema),
    metadata: z.record(z.string(), z.string()).optional(),
  })
  .strict();

/**
 * @alpha
 * Runtime input type accepted by admin permission policy schema validation.
 */
export type AdminPermissionPolicyInputType = z.input<
  typeof AdminPermissionPolicySchema
>;

/**
 * @alpha
 * Runtime output type produced by admin permission policy schema validation.
 */
export type AdminPermissionPolicyOutputType = z.output<
  typeof AdminPermissionPolicySchema
>;
