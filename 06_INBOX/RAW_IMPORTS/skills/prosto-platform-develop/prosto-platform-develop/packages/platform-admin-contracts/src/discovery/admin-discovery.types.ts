import type {
  ADMIN_DISCOVERY_EXTENSION_KINDS,
  ADMIN_DISCOVERY_PAYLOAD_SCHEMA_VERSION,
} from './admin-discovery.constants.js';

/**
 * @alpha
 * Versioned discovery payload schema discriminator.
 */
export type AdminDiscoveryPayloadSchemaVersionType =
  typeof ADMIN_DISCOVERY_PAYLOAD_SCHEMA_VERSION;

/**
 * @alpha
 * Discovery registry group identifier.
 */
export type AdminDiscoveryExtensionKindType =
  (typeof ADMIN_DISCOVERY_EXTENSION_KINDS)[number];

/**
 * @alpha
 * Descriptor identifier within an admin discovery registry.
 */
export type AdminDiscoveryDescriptorIdentifierType = string;

/**
 * @alpha
 * Framework-neutral component key resolved by the admin shell registry.
 */
export type AdminDiscoveryComponentKeyType = string;

/**
 * @alpha
 * Admin shell route path contributed by a page extension.
 */
export type AdminDiscoveryRouteType = string;

/**
 * @alpha
 * Named admin shell layout slot for widget extensions.
 */
export type AdminDiscoveryWidgetSlotType = string;

/**
 * @alpha
 * Action target identifier exposed by admin shell surfaces.
 */
export type AdminDiscoveryActionTargetType = string;

/**
 * @alpha
 * Diagnostic reason code for rejected admin UI plugins.
 */
export type AdminDiscoveryRejectionReasonCodeType = string;
