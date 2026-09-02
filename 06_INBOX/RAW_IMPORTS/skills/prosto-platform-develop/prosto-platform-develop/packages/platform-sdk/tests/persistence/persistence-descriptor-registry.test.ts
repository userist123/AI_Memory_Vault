import {
  PersistenceDescriptorRegistry,
  PersistenceError,
  PersistenceNotReadyError,
  type IPersistenceDescriptor,
} from '@/index.js';
import { describe, expect, it } from 'vitest';

function createDescriptor(
  owner: 'platform' | 'module',
  ownerId: string,
): IPersistenceDescriptor {
  return { owner, ownerId, payload: { fixture: true } };
}

describe('PersistenceDescriptorRegistry', () => {
  it('collects descriptors and seals an immutable snapshot', () => {
    const registry = new PersistenceDescriptorRegistry();
    const descriptor = createDescriptor('module', 'catalog');

    registry.register('catalog', descriptor);
    const sealed = registry.seal();

    expect(sealed).toEqual([descriptor]);
    expect(Object.isFrozen(sealed)).toBe(true);
  });

  it('rolls back descriptors from failed module initialization', () => {
    const registry = new PersistenceDescriptorRegistry();
    registry.register('catalog', createDescriptor('module', 'catalog'));

    registry.rollback('catalog');

    expect(registry.seal()).toEqual([]);
  });

  it('rejects a descriptor registered for another module owner', () => {
    const registry = new PersistenceDescriptorRegistry();

    expect(() =>
      registry.register('catalog', createDescriptor('module', 'orders')),
    ).toThrowError(PersistenceError);

    try {
      registry.register('catalog', createDescriptor('module', 'orders'));
    } catch (error) {
      expect(error).toMatchObject({
        code: 'PersistenceDescriptorOwnerMismatch',
      });
    }
  });

  it('accepts the platform descriptor only through the platform owner', () => {
    const registry = new PersistenceDescriptorRegistry();
    registry.register('platform', createDescriptor('platform', 'platform'));

    expect(registry.seal()).toHaveLength(1);
  });

  it('rejects duplicate descriptors and registrations after sealing', () => {
    const registry = new PersistenceDescriptorRegistry();
    const descriptor = createDescriptor('module', 'catalog');
    registry.register('catalog', descriptor);

    expect(() => registry.register('catalog', descriptor)).toThrowError(
      PersistenceError,
    );

    registry.seal();

    expect(() =>
      registry.register('orders', createDescriptor('module', 'orders')),
    ).toThrowError(PersistenceError);
  });

  it('provides a deterministic not-ready error', () => {
    const error = new PersistenceNotReadyError();

    expect(error.code).toBe('PersistenceProviderNotReady');
    expect(error.details).toMatchObject({ phase: 'not-ready' });
  });
});
