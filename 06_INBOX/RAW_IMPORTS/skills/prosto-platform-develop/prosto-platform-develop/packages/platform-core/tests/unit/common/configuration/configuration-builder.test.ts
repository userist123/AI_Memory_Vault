import { describe, expect, it } from 'vitest';
import { z } from 'zod';
import { ConfigurationBuilder } from '@/common/index.js';

describe('ConfigurationBuilder', () => {
  it('builds empty config with no providers', () => {
    const config = new ConfigurationBuilder().build();

    expect(config).toEqual({});
  });

  it('builds config from a single in-memory provider', () => {
    const config = new ConfigurationBuilder()
      .addInMemoryCollection({ key: 'value', port: 8080 })
      .build();

    expect(config).toEqual({ key: 'value', port: 8080 });
  });

  it('last provider overrides earlier values (priority by order)', () => {
    const config = new ConfigurationBuilder()
      .addInMemoryCollection({ key: 'first', shared: 'from-first' })
      .addInMemoryCollection({ key: 'second', extra: 'added' })
      .build();

    expect(config).toEqual({
      key: 'second',
      shared: 'from-first',
      extra: 'added',
    });
  });

  it('deep merges nested objects from multiple providers', () => {
    const config = new ConfigurationBuilder()
      .addInMemoryCollection({
        database: { host: 'localhost', port: 5432 },
        logging: { level: 'info' },
      })
      .addInMemoryCollection({
        database: { port: 15432, pool: 10 },
        logging: { format: 'json' },
      })
      .build();

    expect(config).toEqual({
      database: { host: 'localhost', port: 15432, pool: 10 },
      logging: { level: 'info', format: 'json' },
    });
  });

  it('arrays are replaced not merged', () => {
    const config = new ConfigurationBuilder()
      .addInMemoryCollection({ items: [1, 2, 3] })
      .addInMemoryCollection({ items: [4, 5] })
      .build();

    expect(config).toEqual({ items: [4, 5] });
  });

  it('null values override existing values', () => {
    const config = new ConfigurationBuilder()
      .addInMemoryCollection({ key: 'value', nested: { inner: 'keep' } })
      .addInMemoryCollection({ key: null })
      .build();

    expect(config).toEqual({ key: null, nested: { inner: 'keep' } });
  });

  it('validates config with inline schema', () => {
    const schema = z.object({
      name: z.string(),
      port: z.number().default(3000),
    });

    const config = new ConfigurationBuilder(schema)
      .addInMemoryCollection({ name: 'test' })
      .build();

    expect(config).toEqual({ name: 'test', port: 3000 });
  });

  it('supports chaining all provider types', () => {
    const originalEnv = process.env['PROSTO_TEST_KEY'];
    process.env['PROSTO_TEST_KEY'] = 'env-val';

    try {
      const config = new ConfigurationBuilder()
        .addInMemoryCollection({ key: 'memory' })
        .addCommandLine(['--cli:flag'])
        .addEnvironmentVariables({ prefix: 'PROSTO_' })
        .build();

      expect(config.key).toBe('memory');
      expect(config.cli).toEqual({ flag: true });
      expect(config.testKey).toBe('env-val');
    } finally {
      if (originalEnv !== undefined) {
        process.env['PROSTO_TEST_KEY'] = originalEnv;
      } else {
        delete process.env['PROSTO_TEST_KEY'];
      }
    }
  });

  it('provider instances are isolated between builds', () => {
    const builder = new ConfigurationBuilder().addInMemoryCollection({
      key: 'value',
    });

    const first = builder.build();
    const second = builder.build();

    expect(first).toEqual(second);
    expect(first).not.toBe(second);
  });
});
