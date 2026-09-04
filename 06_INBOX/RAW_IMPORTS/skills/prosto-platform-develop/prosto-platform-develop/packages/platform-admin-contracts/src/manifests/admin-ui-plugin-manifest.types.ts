import type {
  ADMIN_UI_PLUGIN_EXTENSION_POINTS,
  ADMIN_UI_PLUGIN_MANIFEST_SCHEMA_VERSION,
  ADMIN_UI_PLUGIN_REVIEW_STATUSES,
  ADMIN_UI_PLUGIN_TRUST_CLASSES,
} from './admin-ui-plugin-manifest.constants.js';

/**
 * @alpha
 * Canonical admin UI plugin identity token.
 */
export type AdminUIPluginIdentifierType = string;

/**
 * @alpha
 * Semantic version string for admin UI plugin artifacts.
 */
export type AdminUIPluginVersionType = string;

/**
 * @alpha
 * Semantic version range supported by an admin shell.
 */
export type AdminShellCompatibilityRangeType = string;

/**
 * @alpha
 * Versioned manifest schema discriminator.
 */
export type AdminUIPluginManifestSchemaVersionType =
  typeof ADMIN_UI_PLUGIN_MANIFEST_SCHEMA_VERSION;

/**
 * @alpha
 * Permission token required by an admin UI plugin.
 */
export type AdminUIPluginPermissionType = string;

/**
 * @alpha
 * Capability token required by an admin UI plugin.
 */
export type AdminUIPluginCapabilityType = string;

/**
 * @alpha
 * Framework-neutral extension points supported by admin discovery.
 */
export type AdminUIPluginExtensionPointType =
  (typeof ADMIN_UI_PLUGIN_EXTENSION_POINTS)[number];

/**
 * @alpha
 * Trust class for admin UI plugin governance.
 */
export type AdminUIPluginTrustClassType =
  (typeof ADMIN_UI_PLUGIN_TRUST_CLASSES)[number];

/**
 * @alpha
 * Review status for admin UI plugin admission decisions.
 */
export type AdminUIPluginReviewStatusType =
  (typeof ADMIN_UI_PLUGIN_REVIEW_STATUSES)[number];
