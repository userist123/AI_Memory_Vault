/**
 * @alpha
 * Version identifier for the first admin discovery payload schema.
 */
export const ADMIN_DISCOVERY_PAYLOAD_SCHEMA_VERSION =
  'admin-discovery-payload.v1' as const;

/**
 * @alpha
 * Regex pattern for discovery descriptor identifiers.
 */
export const ADMIN_DISCOVERY_DESCRIPTOR_ID_PATTERN =
  /^[a-z][a-z0-9-]*(?:\.[a-z][a-z0-9-]*)*$/;

/**
 * @alpha
 * Regex pattern for framework-neutral component registry keys.
 */
export const ADMIN_DISCOVERY_COMPONENT_KEY_PATTERN =
  /^[a-z][a-z0-9-]*(?:\.[a-z][a-z0-9-]*)*$/;

/**
 * @alpha
 * Regex pattern for named widget slots in the admin shell layout.
 */
export const ADMIN_DISCOVERY_WIDGET_SLOT_PATTERN =
  /^[a-z][a-z0-9-]*(?:\.[a-z][a-z0-9-]*)*$/;

/**
 * @alpha
 * Regex pattern for action target identifiers.
 */
export const ADMIN_DISCOVERY_ACTION_TARGET_PATTERN =
  /^[a-z][a-z0-9-]*(?:\.[a-z][a-z0-9-]*)*$/;

/**
 * @alpha
 * Regex pattern for diagnostic reason codes.
 */
export const ADMIN_DISCOVERY_REJECTION_REASON_PATTERN = /^[A-Z][A-Z0-9_]*$/;

/**
 * @alpha
 * Extension registry groups returned by admin discovery.
 */
export const ADMIN_DISCOVERY_EXTENSION_KINDS = [
  'navigation',
  'page',
  'widget',
  'action',
] as const;
