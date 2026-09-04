import type {
  IPersistenceDescriptorRegistry,
  PersistenceProviderStateType,
} from '@/persistence/index.js';

/**
 * @alpha
 * Persistence surface exposed in a module context.
 *
 * Descriptors may be registered only from init(). Database queries are forbidden
 * until start(), when an adapter publishes its ready native service token.
 */
export interface IPersistenceModuleContext {
  readonly state: PersistenceProviderStateType | 'unavailable';
  readonly descriptors?: IPersistenceDescriptorRegistry;
}
