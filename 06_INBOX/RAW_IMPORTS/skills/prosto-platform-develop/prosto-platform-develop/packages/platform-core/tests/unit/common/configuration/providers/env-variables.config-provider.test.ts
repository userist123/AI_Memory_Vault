/* eslint-disable @typescript-eslint/no-dynamic-delete */
import { afterEach, describe, expect, it } from 'vitest';
import { EnvironmentVariablesConfigurationProvider } from '@/common/index.js';

const ENV_KEYS: string[] = [];

function setEnv(key: string, value: string) {
  process.env[key] = value;
  ENV_KEYS.push(key);
}

afterEach(() => {
  for (const key of ENV_KEYS) {
    delete process.env[key];
  }

  ENV_KEYS.length = 0;
});

describe('EnvironmentVariablesConfigurationProvider', () => {
  it('loads env vars without prefix', () => {
    setEnv('DB_HOST', 'localhost');
    setEnv('DB_PORT', '5432');

    const provider = new EnvironmentVariablesConfigurationProvider();
    const config = provider.load();

    expect(config).toMatchObject({
      dbHost: 'localhost',
      dbPort: 5432,
    });
  });

  it('filters env vars by prefix', () => {
    setEnv('MYAPP_DB__HOST', 'localhost');
    setEnv('MYAPP_DB__PORT', '5432');
    setEnv('OTHER_VAR', 'should-be-ignored');

    const provider = new EnvironmentVariablesConfigurationProvider({
      prefix: 'MYAPP_',
    });
    const config = provider.load();

    expect(config).toEqual({
      db: { host: 'localhost', port: 5432 },
    });
  });

  it('uses custom separator', () => {
    setEnv('CFG_DB_HOST', 'localhost');
    setEnv('CFG_DB_PORT', '3306');

    const provider = new EnvironmentVariablesConfigurationProvider({
      separator: '_',
    });
    const config = provider.load();

    expect(config).toMatchObject({
      cfg: { db: { host: 'localhost', port: 3306 } },
    });
  });

  it('coerces boolean values', () => {
    setEnv('FEATURE_ENABLED', 'true');
    setEnv('FEATURE_ACTIVE', 'false');

    const provider = new EnvironmentVariablesConfigurationProvider();
    const config = provider.load();

    expect(config).toMatchObject({
      featureEnabled: true,
      featureActive: false,
    });
  });

  it('coerces null value', () => {
    setEnv('NULL_VALUE', 'null');

    const provider = new EnvironmentVariablesConfigurationProvider();
    const config = provider.load();

    expect(config).toMatchObject({ nullValue: null });
  });

  it('coerces numeric values', () => {
    setEnv('TIMEOUT_MS', '30000');
    setEnv('PI', '3.14');

    const provider = new EnvironmentVariablesConfigurationProvider();
    const config = provider.load();

    expect(config).toMatchObject({
      timeoutMs: 30000,
      pi: 3.14,
    });
  });

  it('parses JSON objects', () => {
    setEnv('CONFIG', '{"key":"value","num":42}');

    const provider = new EnvironmentVariablesConfigurationProvider();
    const config = provider.load();

    expect(config).toMatchObject({
      config: { key: 'value', num: 42 },
    });
  });

  it('parses JSON arrays', () => {
    setEnv('ITEMS', '["a","b","c"]');

    const provider = new EnvironmentVariablesConfigurationProvider();
    const config = provider.load();

    expect(config).toMatchObject({
      items: ['a', 'b', 'c'],
    });
  });

  it('parses comma-separated values into array', () => {
    setEnv('TAGS', 'apple,banana,cherry');

    const provider = new EnvironmentVariablesConfigurationProvider();
    const config = provider.load();

    expect(config).toMatchObject({
      tags: ['apple', 'banana', 'cherry'],
    });
  });

  it('transforms snake_case to camelCase in path segments', () => {
    setEnv('MY_VAR_NAME', 'hello');

    const provider = new EnvironmentVariablesConfigurationProvider();
    const config = provider.load();

    expect(config).toMatchObject({ myVarName: 'hello' });
  });

  it('creates nested objects from separator-separated keys', () => {
    setEnv('APP__DB__HOST', 'localhost');
    setEnv('APP__DB__PORT', '5432');
    setEnv('APP__LOGGING__LEVEL', 'debug');

    const provider = new EnvironmentVariablesConfigurationProvider({
      prefix: '',
    });
    const config = provider.load();

    expect(config).toMatchObject({
      app: {
        db: { host: 'localhost', port: 5432 },
        logging: { level: 'debug' },
      },
    });
  });

  it('handles empty prefix gracefully', () => {
    setEnv('SOME_KEY', 'value');

    const provider = new EnvironmentVariablesConfigurationProvider({
      prefix: '',
    });
    const config = provider.load();

    expect(config).toMatchObject({ someKey: 'value' });
  });
});
