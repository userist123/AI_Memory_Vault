import { describe, expect, it } from 'vitest';
import { InMemoryConfigurationProvider } from '@/common/index.js';

describe('InMemoryConfigurationProvider', () => {
  it('returns the provided config', () => {
    const input = { key: 'value', port: 8080 };
    const provider = new InMemoryConfigurationProvider(input);
    const config = provider.load();

    expect(config).toEqual(input);
  });

  it('returns a deep clone, not the same reference', () => {
    const input = { nested: { key: 'value' } };
    const provider = new InMemoryConfigurationProvider(input);
    const config = provider.load();

    expect(config).not.toBe(input);
    expect(config.nested).not.toBe(input.nested);
  });

  it('returns empty object for empty config', () => {
    const provider = new InMemoryConfigurationProvider({});
    const config = provider.load();

    expect(config).toEqual({});
  });

  it('supports various value types', () => {
    const input = {
      string: 'hello',
      number: 42,
      boolean: true,
      nullValue: null,
      array: [1, 2, 3],
      nested: { inner: 'value' },
    };
    const provider = new InMemoryConfigurationProvider(input);
    const config = provider.load();

    expect(config).toEqual(input);
  });

  it('mutation of returned config does not affect source', () => {
    const input = { key: 'original' };
    const provider = new InMemoryConfigurationProvider(input);
    const config = provider.load();

    config.key = 'mutated';

    expect(input.key).toBe('original');
    expect(config.key).toBe('mutated');
  });

  it('mutation of source does not affect previously returned config', () => {
    const input: Record<string, unknown> = { key: 'original' };
    const provider = new InMemoryConfigurationProvider(input);
    const config = provider.load();

    input.key = 'changed';

    expect(config.key).toBe('original');
  });

  it('handles deeply nested objects', () => {
    const input = {
      level1: {
        level2: {
          level3: {
            value: 'deep',
          },
        },
      },
    };
    const provider = new InMemoryConfigurationProvider(input);
    const config = provider.load();

    expect(config).toEqual(input);
    expect(config.level1).not.toBe(input.level1);
    expect((config.level1 as Record<string, unknown>).level2).not.toBe(
      input.level1.level2,
    );
  });
});
