import { describe, expect, it } from 'vitest';
import { createServiceToken } from '@prosto/platform-sdk';
import {
  InMemoryServiceRegistry,
  ServiceAlreadyRegisteredError,
  ServiceNotFoundError,
} from '@/services/index.js';

const TOKEN_A = createServiceToken<string>('token-a');
const TOKEN_B = createServiceToken<string>('token-b');

describe('InMemoryServiceRegistry', () => {
  it('registers and resolves a service', () => {
    const registry = new InMemoryServiceRegistry();

    registry.register(TOKEN_A, 'service-a');

    expect(registry.resolve(TOKEN_A)).toBe('service-a');
  });

  it('throws ServiceAlreadyRegisteredError on duplicate registration', () => {
    const registry = new InMemoryServiceRegistry();

    registry.register(TOKEN_A, 'first');

    expect(() => registry.register(TOKEN_A, 'second')).toThrow(
      ServiceAlreadyRegisteredError,
    );
  });

  it('overrides an existing service', () => {
    const registry = new InMemoryServiceRegistry();

    registry.register(TOKEN_A, 'first');
    registry.override(TOKEN_A, 'second');

    expect(registry.resolve(TOKEN_A)).toBe('second');
  });

  it('throws ServiceNotFoundError when overriding non-existent token', () => {
    const registry = new InMemoryServiceRegistry();

    expect(() => registry.override(TOKEN_A, 'value')).toThrow(
      ServiceNotFoundError,
    );
  });

  it('returns undefined for unregistered token', () => {
    const registry = new InMemoryServiceRegistry();

    expect(registry.resolve(TOKEN_A)).toBeUndefined();
  });

  it('returns ServiceNotFoundError for unregistered token', () => {
    const registry = new InMemoryServiceRegistry();

    expect(() => registry.resolveRequired(TOKEN_A)).toThrow(
      ServiceNotFoundError,
    );
  });

  it('checks token existence', () => {
    const registry = new InMemoryServiceRegistry();

    expect(registry.has(TOKEN_A)).toBe(false);

    registry.register(TOKEN_A, 'value');

    expect(registry.has(TOKEN_A)).toBe(true);
  });

  it('unregisters a service', () => {
    const registry = new InMemoryServiceRegistry();

    registry.register(TOKEN_A, 'value');
    registry.unregister(TOKEN_A);

    expect(registry.has(TOKEN_A)).toBe(false);
  });

  it('clears all services', () => {
    const registry = new InMemoryServiceRegistry();

    registry.register(TOKEN_A, 'a');
    registry.register(TOKEN_B, 'b');
    registry.dispose();

    expect(registry.has(TOKEN_A)).toBe(false);
    expect(registry.has(TOKEN_B)).toBe(false);
  });
});
