/**
 * @alpha
 * Identifies whether a descriptor belongs to the platform or a module.
 */
export type PersistenceOwnerType = 'platform' | 'module';

/**
 * @alpha
 * Opaque adapter-owned persistence metadata.
 */
export type PersistenceDescriptorPayloadType = unknown;

/**
 * @alpha
 * Generic persistence declaration collected during module init.
 */
export interface IPersistenceDescriptor {
  readonly owner: PersistenceOwnerType;
  readonly ownerId: string;
  readonly payload: PersistenceDescriptorPayloadType;
  readonly requiredDriverCapabilities?: readonly string[];
}
