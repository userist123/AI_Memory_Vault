import type { IServiceRegistry } from '@/services/index.js';
import type { IPersistenceDescriptor } from './persistence-descriptor.interface.js';

/**
 * @alpha
 * Input supplied after the descriptor collection is sealed.
 */
export interface IPersistenceInitializationInput {
  readonly descriptors: readonly IPersistenceDescriptor[];
  readonly configuration: { typeorm: Readonly<Record<string, unknown>> };
  readonly services: IServiceRegistry;
}
