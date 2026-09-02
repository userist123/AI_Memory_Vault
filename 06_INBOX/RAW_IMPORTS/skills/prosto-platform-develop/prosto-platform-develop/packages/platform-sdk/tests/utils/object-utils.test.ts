import { describe, expect, it } from 'vitest';
import {
  collectKeys,
  createConfigObject,
  isPlainObject,
  resolveNestedValue,
  setNestedValue,
} from '@/index.js';

describe('isPlainObject', () => {
  it('returns true for plain object', () => {
    expect(isPlainObject({ a: 1 })).toBe(true);
  });

  it('returns false for null', () => {
    expect(isPlainObject(null)).toBe(false);
  });

  it('returns false for undefined', () => {
    expect(isPlainObject(undefined)).toBe(false);
  });

  it('returns false for NaN', () => {
    expect(isPlainObject(NaN)).toBe(false);
  });

  it('returns false for arrays', () => {
    expect(isPlainObject([1, 2, 3])).toBe(false);
  });

  it('returns false for primitives', () => {
    expect(isPlainObject('x')).toBe(false);
    expect(isPlainObject(123)).toBe(false);
    expect(isPlainObject(false)).toBe(false);
  });
});

describe('resolveNestedValue', () => {
  const data = {
    a: { b: { c: 42 } },
    empty: null,
  } satisfies Record<string, unknown>;

  it('resolves existing nested value', () => {
    expect(resolveNestedValue<number>(data, 'a.b.c')).toBe(42);
  });

  it('returns undefined for missing path', () => {
    expect(resolveNestedValue(data, 'a.b.missing')).toBeUndefined();
  });

  it('returns undefined for empty key', () => {
    expect(resolveNestedValue(data, '')).toBeUndefined();
  });

  it('returns undefined when traversal meets non-object', () => {
    expect(resolveNestedValue(data, 'empty.value')).toBeUndefined();
  });
});

describe('setNestedValue', () => {
  it('sets nested value with default separator', () => {
    const target: Record<string, unknown> = {};

    setNestedValue(target, 'a.b.c', 100);

    expect(target).toEqual({ a: { b: { c: 100 } } });
  });

  it('overwrites non-object intermediate nodes', () => {
    const target: Record<string, unknown> = { a: 1 };

    setNestedValue(target, 'a.b', 2);

    expect(target).toEqual({ a: { b: 2 } });
  });

  it('supports custom path separator', () => {
    const target: Record<string, unknown> = {};

    setNestedValue(target, 'a/b/c', 'ok', { pathSeparator: '/' });

    expect(target).toEqual({ a: { b: { c: 'ok' } } });
  });
});

describe('collectKeys', () => {
  it('collects flat keys', () => {
    expect(collectKeys({ a: 1, b: 2 })).toEqual(['a', 'b']);
  });

  it('collects nested keys recursively', () => {
    expect(collectKeys({ a: { b: { c: 1 } } })).toEqual(['a', 'a.b', 'a.b.c']);
  });

  it('supports prefix', () => {
    expect(collectKeys({ b: { c: 1 } }, 'a')).toEqual(['a.b', 'a.b.c']);
  });

  it('does not recurse into arrays', () => {
    expect(collectKeys({ arr: [1, 2] })).toEqual(['arr']);
  });
});

describe('createConfigObject', () => {
  it('returns object with get/getValue/getSection/has/keys helpers', () => {
    const config = createConfigObject({
      server: {
        host: 'localhost',
        port: 3000,
      },
      features: {
        auth: true,
      },
    });

    expect(config.get<string>('server.host')).toBe('localhost');
    expect(config.getValue('server.timeout', 5000)).toBe(5000);
    expect(config.getSection<{ host: string; port: number }>('server')).toEqual(
      {
        host: 'localhost',
        port: 3000,
      },
    );
    expect(config.has('features.auth')).toBe(true);
    expect(config.has('features.missing')).toBe(false);
    expect(config.keys()).toEqual([
      'server',
      'server.host',
      'server.port',
      'features',
      'features.auth',
    ]);
    expect(config.keys('server')).toEqual(['host', 'port']);
  });

  it('get with empty key returns entire object', () => {
    const config = createConfigObject({ a: { b: 1 } });

    expect(config.get('')).toEqual(config);
  });

  it('keys returns empty array for non-object section', () => {
    const config = createConfigObject({ a: 1 });

    expect(config.keys('a')).toEqual([]);
  });
});
