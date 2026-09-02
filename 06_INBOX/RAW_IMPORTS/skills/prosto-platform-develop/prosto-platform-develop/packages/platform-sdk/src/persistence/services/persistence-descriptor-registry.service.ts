import type {
  IPersistenceDescriptor,
  IPersistenceDescriptorRegistry,
} from '@/persistence/interfaces/index.js';
import { PersistenceError } from '../errors/index.js';

/**
 * @alpha
 * In-memory registry enforcing descriptor ownership and immutability.
 */
export class PersistenceDescriptorRegistry implements IPersistenceDescriptorRegistry {
  private readonly _descriptors = new Map<string, IPersistenceDescriptor>();
  private _sealed = false;

  register(moduleId: string, descriptor: IPersistenceDescriptor): void {
    if (this._sealed) {
      throw new PersistenceError(
        'PersistenceRegistryNotCollecting',
        'Persistence descriptors can only be registered while collection is open.',
        {
          moduleId,
          phase: 'sealed',
          remediationHint:
            'Register the descriptor from the module init() lifecycle method.',
        },
      );
    }

    const expectedOwner =
      descriptor.owner === 'platform' ? 'platform' : moduleId;

    if (
      descriptor.ownerId !== expectedOwner ||
      (descriptor.owner === 'platform' && moduleId !== 'platform')
    ) {
      throw new PersistenceError(
        'PersistenceDescriptorOwnerMismatch',
        `Persistence descriptor owner "${descriptor.ownerId}" does not match registering owner "${expectedOwner}".`,
        {
          moduleId,
          ownerId: descriptor.ownerId,
          phase: 'collecting',
          remediationHint:
            'A module descriptor must use its context moduleId; only platform composition can register the platform descriptor.',
        },
      );
    }

    if (this._descriptors.has(moduleId)) {
      throw new PersistenceError(
        'PersistenceDuplicateDescriptor',
        `A persistence descriptor is already registered for "${moduleId}".`,
        { moduleId, ownerId: descriptor.ownerId, phase: 'collecting' },
      );
    }

    this._descriptors.set(moduleId, descriptor);
  }

  rollback(moduleId: string): void {
    if (this._sealed) {
      throw new PersistenceError(
        'PersistenceRegistryNotCollecting',
        'Persistence descriptors cannot be rolled back after collection is sealed.',
        { moduleId, phase: 'sealed' },
      );
    }

    this._descriptors.delete(moduleId);
  }

  seal(): readonly IPersistenceDescriptor[] {
    this._sealed = true;

    return Object.freeze([...this._descriptors.values()]);
  }
}
