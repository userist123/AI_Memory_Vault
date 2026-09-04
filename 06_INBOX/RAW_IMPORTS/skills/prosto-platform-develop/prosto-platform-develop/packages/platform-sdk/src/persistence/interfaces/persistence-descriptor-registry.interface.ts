import type { IPersistenceDescriptor } from './persistence-descriptor.interface.js';

/**
 * @alpha
 * Collects persistence declarations during module initialization.
 */
export interface IPersistenceDescriptorRegistry {
  /**
   * Registers a persistence descriptor.
   */
  register(moduleId: string, descriptor: IPersistenceDescriptor): void;

  /**
   * Rolls back all persistence declarations registered for the given module.
   */
  rollback(moduleId: string): void;

  /**
   * Seals the persistence descriptor registry.
   */
  seal(): readonly IPersistenceDescriptor[];
}
