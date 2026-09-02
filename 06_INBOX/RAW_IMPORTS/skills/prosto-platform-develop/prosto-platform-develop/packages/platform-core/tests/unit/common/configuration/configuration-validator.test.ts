import { describe, expect, it } from 'vitest';
import { z } from 'zod';
import {
  ConfigurationValidator,
  ConfigurationValidationError,
} from '@/common/index.js';

const testSchema = z.object({
  name: z.string(),
  age: z.number().positive(),
  active: z.boolean().default(false),
});

describe('ConfigValidator', () => {
  const configValidator = new ConfigurationValidator();

  it('validates correct config successfully', () => {
    const result = configValidator.validate(
      { name: 'test', age: 25, active: true },
      testSchema,
    );

    expect(result).toEqual({ name: 'test', age: 25, active: true });
  });

  it('applies default values', () => {
    const result = configValidator.validate(
      { name: 'test', age: 25 },
      testSchema,
    );

    expect(result.active).toBe(false);
  });

  it('throws ConfigValidationError for invalid config', () => {
    expect(() =>
      configValidator.validate({ name: 'test', age: -1 }, testSchema),
    ).toThrow(ConfigurationValidationError);
  });

  it('throws ConfigValidationError for missing required fields', () => {
    expect(() =>
      configValidator.validate({ name: 'test' }, testSchema),
    ).toThrow(ConfigurationValidationError);
  });

  it('includes path in error messages', () => {
    try {
      configValidator.validate({ name: 'test', age: -5 }, testSchema);
    } catch (error) {
      expect(error).toBeInstanceOf(ConfigurationValidationError);
      const validationError = error as ConfigurationValidationError;
      expect(validationError.message).toContain('age');
      expect(validationError.zodError).toBeDefined();
    }
  });

  it('rejects extra keys when schema is strict', () => {
    const strictSchema = z
      .object({
        name: z.string(),
      })
      .strict();

    expect(() =>
      configValidator.validate({ name: 'test', extra: 'field' }, strictSchema),
    ).toThrow(ConfigurationValidationError);
  });

  it('handles empty config with defaults-only schema', () => {
    const defaultsSchema = z.object({
      port: z.number().default(3000),
      host: z.string().default('localhost'),
    });

    const result = configValidator.validate({}, defaultsSchema);
    expect(result).toEqual({ port: 3000, host: 'localhost' });
  });

  it('validates nested object schemas', () => {
    const nestedSchema = z.object({
      database: z.object({
        host: z.string(),
        port: z.number(),
      }),
    });

    const result = configValidator.validate(
      { database: { host: 'localhost', port: 5432 } },
      nestedSchema,
    );
    expect(result.database).toEqual({ host: 'localhost', port: 5432 });
  });

  it('transforms values via schema refinements', () => {
    const trimmedSchema = z.object({
      name: z.string().transform((s) => s.trim()),
    });

    const result = configValidator.validate(
      { name: '  hello  ' },
      trimmedSchema,
    );
    expect(result.name).toBe('hello');
  });
});
