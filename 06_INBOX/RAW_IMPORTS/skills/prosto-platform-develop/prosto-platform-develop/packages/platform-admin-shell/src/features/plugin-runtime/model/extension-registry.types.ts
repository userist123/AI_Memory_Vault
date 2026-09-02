import type { AdminDiscoveryExtensionKindType } from '@prosto/platform-admin-contracts';

/**
 * @alpha
 * Base shape shared by all extension descriptors.
 */
export interface IExtensionDescriptorBase {
  readonly id: string;
  readonly pluginId: string;
  readonly order?: number;
}

/**
 * @alpha
 * Conflict reason detected during extension registration.
 */
export type ExtensionConflictReasonType =
  | 'DUPLICATE_ID'
  | 'DUPLICATE_ROUTE'
  | 'DUPLICATE_SLOT'
  | 'DUPLICATE_ACTION';

/**
 * @alpha
 * Conflict detected during extension registration.
 */
export interface IExtensionConflict {
  readonly kind: AdminDiscoveryExtensionKindType;
  readonly existingDescriptorId: string;
  readonly existingPluginId: string;
  readonly conflictingDescriptorId: string;
  readonly conflictingPluginId: string;
  readonly reason: ExtensionConflictReasonType;
  readonly detail: string;
}

/**
 * @alpha
 * Registration result for a single plugin's extensions.
 */
export interface IPluginExtensionRegistrationResult {
  readonly registered: boolean;
  readonly conflicts: readonly IExtensionConflict[];
  readonly registeredDescriptorIds: readonly string[];
}
