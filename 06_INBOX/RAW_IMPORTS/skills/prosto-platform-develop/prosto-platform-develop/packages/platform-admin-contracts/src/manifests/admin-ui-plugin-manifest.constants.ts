/**
 * @alpha
 * Version identifier for the first admin UI plugin manifest schema.
 */
export const ADMIN_UI_PLUGIN_MANIFEST_SCHEMA_VERSION =
  'admin-ui-plugin-manifest.v1' as const;

/**
 * @alpha
 * Regex pattern for admin UI plugin identifiers.
 */
export const ADMIN_UI_PLUGIN_ID_PATTERN = /^[a-z][a-z0-9-]{2,}$/;

/**
 * @alpha
 * Regex pattern for admin permission declarations.
 */
export const ADMIN_UI_PLUGIN_PERMISSION_PATTERN =
  /^[a-z][a-z0-9-]*(?:\.[a-z][a-z0-9-_]*)*(?::[a-z][a-z0-9-_]*)?$/;

/**
 * @alpha
 * Regex pattern for admin capability declarations.
 */
export const ADMIN_UI_PLUGIN_CAPABILITY_PATTERN =
  /^[a-z][a-z0-9-]*(?:\.[a-z][a-z0-9-_]*)*$/;

/**
 * @alpha
 * Framework-neutral extension point taxonomy exposed to admin UI plugins.
 */
export const ADMIN_UI_PLUGIN_EXTENSION_POINTS = [
  'nav',
  'page',
  'widget',
  'action',
] as const;

/**
 * @alpha
 * Trust class taxonomy for admin UI plugin governance.
 */
export const ADMIN_UI_PLUGIN_TRUST_CLASSES = [
  'trusted',
  'internal',
  'third-party-reviewed',
] as const;

/**
 * @alpha
 * Review states for admin UI plugin admission metadata.
 */
export const ADMIN_UI_PLUGIN_REVIEW_STATUSES = [
  'pending',
  'approved',
  'rejected',
  'revoked',
] as const;

/**
 * @alpha
 * Metadata key used by discovery extension descriptors to declare
 * per-extension permission requirements as a JSON-encoded string array.
 */
export const PERMISSION_METADATA_KEY = 'requiredPermissions';

/**
 * @alpha
 * Metadata key used by discovery extension descriptors to declare
 * the match strategy for evaluating permission requirements.
 */
export const PERMISSION_MATCH_METADATA_KEY = 'permissionMatchStrategy';

/**
 * @alpha
 * Metadata key used by discovery extension descriptors to declare
 * per-extension capability requirements as a JSON-encoded string array.
 */
export const CAPABILITY_METADATA_KEY = 'requiredCapabilities';
