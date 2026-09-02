import { z } from 'zod';
import {
  ADMIN_UI_PLUGIN_ID_PATTERN,
  ADMIN_UI_PLUGIN_REVIEW_STATUSES,
  ADMIN_UI_PLUGIN_TRUST_CLASSES,
  AdminShellCompatibilityRangeSchema,
  AdminUIPluginSemverVersionSchema,
} from '../manifests/index.js';
import {
  ADMIN_DISCOVERY_ACTION_TARGET_PATTERN,
  ADMIN_DISCOVERY_COMPONENT_KEY_PATTERN,
  ADMIN_DISCOVERY_DESCRIPTOR_ID_PATTERN,
  ADMIN_DISCOVERY_PAYLOAD_SCHEMA_VERSION,
  ADMIN_DISCOVERY_REJECTION_REASON_PATTERN,
  ADMIN_DISCOVERY_WIDGET_SLOT_PATTERN,
} from './admin-discovery.constants.js';

/**
 * @alpha
 * Zod schema for discovery descriptor identifiers.
 */
export const AdminDiscoveryDescriptorIdSchema = z
  .string()
  .regex(ADMIN_DISCOVERY_DESCRIPTOR_ID_PATTERN, {
    message: 'Descriptor id must use dot-separated lowercase segments.',
  });

/**
 * @alpha
 * Zod schema for framework-neutral component registry keys.
 */
export const AdminDiscoveryComponentKeySchema = z
  .string()
  .regex(ADMIN_DISCOVERY_COMPONENT_KEY_PATTERN, {
    message: 'Component key must use dot-separated lowercase segments.',
  });

/**
 * @alpha
 * Zod schema for admin shell route paths.
 */
export const AdminDiscoveryRouteSchema = z.string().regex(/^\/[^\s]*$/, {
  message: 'Route must start with "/" and must not contain whitespace.',
});

/**
 * @alpha
 * Zod schema for widget slot identifiers.
 */
export const AdminDiscoveryWidgetSlotSchema = z
  .string()
  .regex(ADMIN_DISCOVERY_WIDGET_SLOT_PATTERN, {
    message: 'Widget slot must use dot-separated lowercase segments.',
  });

/**
 * @alpha
 * Zod schema for action target identifiers.
 */
export const AdminDiscoveryActionTargetSchema = z
  .string()
  .regex(ADMIN_DISCOVERY_ACTION_TARGET_PATTERN, {
    message: 'Action target must use dot-separated lowercase segments.',
  });

/**
 * @alpha
 * Zod schema for discovery rejection reason codes.
 */
export const AdminDiscoveryRejectionReasonCodeSchema = z
  .string()
  .regex(ADMIN_DISCOVERY_REJECTION_REASON_PATTERN, {
    message: 'Reason code must use uppercase snake case.',
  });

const AdminExtensionDescriptorMetadataSchema = z.object({
  id: AdminDiscoveryDescriptorIdSchema,
  pluginId: z.string().regex(ADMIN_UI_PLUGIN_ID_PATTERN, {
    message: 'Plugin id must match admin UI plugin id pattern.',
  }),
  order: z.number().int().optional(),
  metadata: z.record(z.string(), z.string()).optional(),
});

/**
 * @alpha
 * Zod schema for navigation extension descriptors.
 */
export const AdminNavigationExtensionDescriptorSchema =
  AdminExtensionDescriptorMetadataSchema.extend({
    label: z.string().min(1),
    icon: z.string().min(1).optional(),
    parentId: AdminDiscoveryDescriptorIdSchema.optional(),
    pageId: AdminDiscoveryDescriptorIdSchema.optional(),
  }).strict();

/**
 * @alpha
 * Zod schema for page extension descriptors.
 */
export const AdminPageExtensionDescriptorSchema =
  AdminExtensionDescriptorMetadataSchema.extend({
    route: AdminDiscoveryRouteSchema,
    title: z.string().min(1),
    componentKey: AdminDiscoveryComponentKeySchema,
    navigationId: AdminDiscoveryDescriptorIdSchema.optional(),
  }).strict();

/**
 * @alpha
 * Zod schema for widget extension descriptors.
 */
export const AdminWidgetExtensionDescriptorSchema =
  AdminExtensionDescriptorMetadataSchema.extend({
    slot: AdminDiscoveryWidgetSlotSchema,
    componentKey: AdminDiscoveryComponentKeySchema,
    title: z.string().min(1).optional(),
  }).strict();

/**
 * @alpha
 * Zod schema for action extension descriptors.
 */
export const AdminActionExtensionDescriptorSchema =
  AdminExtensionDescriptorMetadataSchema.extend({
    target: AdminDiscoveryActionTargetSchema,
    label: z.string().min(1),
    actionKey: AdminDiscoveryComponentKeySchema,
    confirmationRequired: z.boolean().optional(),
  }).strict();

/**
 * @alpha
 * Zod schema for grouped admin UI plugin extension descriptors.
 */
export const AdminPluginDiscoveryExtensionsSchema = z
  .object({
    navigation: z.array(AdminNavigationExtensionDescriptorSchema),
    pages: z.array(AdminPageExtensionDescriptorSchema),
    widgets: z.array(AdminWidgetExtensionDescriptorSchema),
    actions: z.array(AdminActionExtensionDescriptorSchema),
  })
  .strict();

/**
 * @alpha
 * Zod schema for plugins admitted into admin discovery.
 */
export const AdminDiscoveredPluginDescriptorSchema = z
  .object({
    id: z.string().regex(ADMIN_UI_PLUGIN_ID_PATTERN, {
      message: 'Plugin id must match admin UI plugin id pattern.',
    }),
    version: AdminUIPluginSemverVersionSchema,
    displayName: z.string().min(1).optional(),
    shellCompatibility: AdminShellCompatibilityRangeSchema,
    trustClass: z.enum(ADMIN_UI_PLUGIN_TRUST_CLASSES),
    reviewStatus: z.enum(ADMIN_UI_PLUGIN_REVIEW_STATUSES),
    extensions: AdminPluginDiscoveryExtensionsSchema,
    metadata: z.record(z.string(), z.string()).optional(),
  })
  .strict();

/**
 * @alpha
 * Zod schema for rejected admin UI plugin diagnostics.
 */
export const AdminRejectedPluginDiagnosticSchema = z
  .object({
    id: z
      .string()
      .regex(ADMIN_UI_PLUGIN_ID_PATTERN, {
        message: 'Plugin id must match admin UI plugin id pattern.',
      })
      .optional(),
    version: AdminUIPluginSemverVersionSchema.optional(),
    reasonCode: AdminDiscoveryRejectionReasonCodeSchema,
    message: z.string().min(1),
    remediationHint: z.string().min(1),
    details: z.record(z.string(), z.string()).optional(),
  })
  .strict();

/**
 * @alpha
 * Zod schema for versioned admin discovery payloads.
 */
export const AdminDiscoveryPayloadSchema = z
  .object({
    schemaVersion: z.literal(ADMIN_DISCOVERY_PAYLOAD_SCHEMA_VERSION),
    generatedAt: z.string().datetime({ offset: true }),
    plugins: z.array(AdminDiscoveredPluginDescriptorSchema),
    rejected: z.array(AdminRejectedPluginDiagnosticSchema),
  })
  .strict();

/**
 * @alpha
 * Runtime input type accepted by admin discovery payload schema validation.
 */
export type AdminDiscoveryPayloadInputType = z.input<
  typeof AdminDiscoveryPayloadSchema
>;

/**
 * @alpha
 * Runtime output type produced by admin discovery payload schema validation.
 */
export type AdminDiscoveryPayloadOutputType = z.output<
  typeof AdminDiscoveryPayloadSchema
>;
