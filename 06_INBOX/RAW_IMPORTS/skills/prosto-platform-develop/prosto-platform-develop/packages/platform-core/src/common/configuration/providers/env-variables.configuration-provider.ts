import type {
  IConfigurationProvider,
  IEnvOptions,
} from '../interfaces/index.js';
import {
  isPlainObject,
  setNestedValue,
  snakeToCamel,
} from '@prosto/platform-sdk';

type PrimitiveType = string | number | boolean | null;
type ConfigValueType =
  | PrimitiveType
  | ConfigValueType[]
  | { [key: string]: ConfigValueType };

export class EnvironmentVariablesConfigurationProvider implements IConfigurationProvider {
  constructor(private readonly _options: IEnvOptions = {}) {}

  load(): Record<string, unknown> {
    const { prefix = '', separator = '__' } = this._options;
    const result: Record<string, unknown> = {};

    for (const [key, value] of Object.entries(process.env)) {
      if (value === undefined) continue;
      if (prefix && !key.startsWith(prefix)) continue;

      const normalized = prefix ? key.slice(prefix.length) : key;
      const path = normalized
        .split(separator)
        .map((part) => snakeToCamel(part))
        .join('.');

      if (path) {
        setNestedValue(result, path, this._parseValue(value), {
          pathSeparator: '.',
        });
      }
    }

    return result;
  }

  private _parseScalar(raw: string): string | number | boolean | null {
    const value = raw.trim();

    if (value === 'null') return null;
    if (value === 'true') return true;
    if (value === 'false') return false;

    if (/^-?(0|[1-9]\d*)(\.\d+)?$/.test(value)) {
      const n = Number(value);

      if (Number.isFinite(n)) return n;
    }

    return value;
  }

  private _parseValue(raw: string): ConfigValueType {
    const value = raw.trim();

    if (!value) return '';

    if (
      (value.startsWith('{') && value.endsWith('}')) ||
      (value.startsWith('[') && value.endsWith(']')) ||
      value === 'null' ||
      value === 'true' ||
      value === 'false' ||
      /^-?(0|[1-9]\d*)(\.\d+)?$/.test(value)
    ) {
      try {
        const parsed = JSON.parse(value);

        if (Array.isArray(parsed)) {
          return parsed.map((item) =>
            typeof item === 'string' ? this._parseValue(item) : item,
          );
        }

        if (isPlainObject(parsed)) {
          return parsed as Record<string, ConfigValueType>;
        }

        return parsed as PrimitiveType;
      } catch {
        return this._parseScalar(raw);
      }
    }

    if (value.includes(',')) {
      const parts = value
        .split(',')
        .map((s) => s.trim())
        .filter(Boolean);

      if (parts.length > 1) return parts.map((v) => this._parseValue(v));
    }

    return this._parseScalar(raw);
  }
}
