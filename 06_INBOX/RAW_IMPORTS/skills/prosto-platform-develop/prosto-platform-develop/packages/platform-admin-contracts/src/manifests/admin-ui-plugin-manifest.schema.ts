import { valid, validRange } from 'semver';
import { z } from 'zod';
import {
  ADMIN_UI_PLUGIN_CAPABILITY_PATTERN,
  ADMIN_UI_PLUGIN_EXTENSION_POINTS,
  ADMIN_UI_PLUGIN_ID_PATTERN,
  ADMIN_UI_PLUGIN_MANIFEST_SCHEMA_VERSION,
  ADMIN_UI_PLUGIN_PERMISSION_PATTERN,
  ADMIN_UI_PLUGIN_REVIEW_STATUSES,
  ADMIN_UI_PLUGIN_TRUST_CLASSES,
} from './admin-ui-plugin-manifest.constants.js';

/**
 * @alpha
 * Zod schema for semver version strings.
 */
export const AdminUIPluginSemverVersionSchema = z
  .string()
  .refine((value) => valid(value) !== null, {
    message: 'Value must be a valid semver version.',
  });

/**
 * @alpha
 * Zod schema for semver range expressions.
 */
export const AdminShellCompatibilityRangeSchema = z
  .string()
  .refine((value) => validRange(value) !== null, {
    message: 'Value must be a valid semver range.',
  });

/**
 * @alpha
 * Zod schema for admin permission tokens.
 */
export const AdminUIPluginPermissionSchema = z
  .string()
  .regex(ADMIN_UI_PLUGIN_PERMISSION_PATTERN, {
    message:
      'Permission must use dot-separated lowercase segments with an optional action suffix.',
  });

/**
 * @alpha
 * Zod schema for admin capability tokens.
 */
export const AdminUIPluginCapabilitySchema = z
  .string()
  .regex(ADMIN_UI_PLUGIN_CAPABILITY_PATTERN, {
    message: 'Capability must use dot-separated lowercase segments.',
  });

/**
 * @alpha
 * Zod schema for versioned admin UI plugin manifests.
 */
export const AdminUIPluginManifestSchema = z
  .object({
    schemaVersion: z.literal(ADMIN_UI_PLUGIN_MANIFEST_SCHEMA_VERSION),
    id: z.string().regex(ADMIN_UI_PLUGIN_ID_PATTERN, {
      message: 'Plugin id must match admin UI plugin id pattern.',
    }),
    version: AdminUIPluginSemverVersionSchema,
    displayName: z.string().min(1).optional(),
    shellCompatibility: AdminShellCompatibilityRangeSchema,
    requiredPermissions: z.array(AdminUIPluginPermissionSchema),
    requiredCapabilities: z.array(AdminUIPluginCapabilitySchema),
    extensionPoints: z.array(z.enum(ADMIN_UI_PLUGIN_EXTENSION_POINTS)).min(1),
    trustClass: z.enum(ADMIN_UI_PLUGIN_TRUST_CLASSES),
    reviewStatus: z.enum(ADMIN_UI_PLUGIN_REVIEW_STATUSES),
    reviewedAt: z.string().datetime({ offset: true }).optional(),
    reviewer: z.string().min(1).optional(),
    metadata: z.record(z.string(), z.string()).optional(),
  })
  .strict();

/**
 * @alpha
 * Runtime input type accepted by admin UI plugin manifest schema validation.
 */
export type AdminUIPluginManifestInputType = z.input<
  typeof AdminUIPluginManifestSchema
>;

/**
 * @alpha
 * Runtime output type produced by admin UI plugin manifest schema validation.
 */
export type AdminUIPluginManifestOutputType = z.output<
  typeof AdminUIPluginManifestSchema
>;
