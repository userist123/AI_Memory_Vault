import type { IPersistenceDescriptorRegistry } from './persistence-descriptor-registry.interface.js';
import type { IPersistenceInitializationInput } from './persistence-initialization-input.interface.js';

/**
 * @alpha
 * Lifecycle state of a persistence provider.
 */
export type PersistenceProviderStateType =
  | 'collecting'
  | 'initializing'
  | 'ready'
  | 'failed'
  | 'disposed';

/**
 * @alpha
 * Shared persistence adapter lifecycle contract.
 * */
export interface IPersistenceProvider {
  readonly state: PersistenceProviderStateType;
  readonly descriptors: IPersistenceDescriptorRegistry;
  initialize(input: IPersistenceInitializationInput): Promise<void>;
  dispose(): Promise<void>;
}
