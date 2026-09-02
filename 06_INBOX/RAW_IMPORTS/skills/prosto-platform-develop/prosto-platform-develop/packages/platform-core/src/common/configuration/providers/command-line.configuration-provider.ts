import type { IConfigurationProvider } from '../interfaces/index.js';
import { isPlainObject, setNestedValue } from '@prosto/platform-sdk';

type PrimitiveType = string | number | boolean | null;
type ConfigValueType =
  | PrimitiveType
  | ConfigValueType[]
  | { [key: string]: ConfigValueType };

export class CommandLineConfigurationProvider implements IConfigurationProvider {
  constructor(private readonly _args: string[]) {}

  load(): Record<string, unknown> {
    const pathSeparator = ':';
    const result: Record<string, unknown> = {};

    for (let i = 0, l = this._args.length; i < l; i++) {
      const arg = this._args[i] as string;

      if (!arg.startsWith('--') && !arg.startsWith('-')) continue;

      const argWithoutPrefix = arg.startsWith('--')
        ? arg.slice(2)
        : arg.slice(1);

      const eqIndex = argWithoutPrefix.indexOf('=');

      if (eqIndex !== -1) {
        const path = argWithoutPrefix.slice(0, eqIndex);
        const value = argWithoutPrefix.slice(eqIndex + 1);

        setNestedValue(result, path, this._parseValue(value), {
          pathSeparator,
        });
      } else if (i + 1 < l) {
        const nextArg = this._args[i + 1];

        if (nextArg !== undefined && !nextArg.startsWith('-')) {
          setNestedValue(result, argWithoutPrefix, this._parseValue(nextArg), {
            pathSeparator,
          });

          i++;
        } else {
          setNestedValue(result, argWithoutPrefix, true, { pathSeparator });
        }
      } else {
        setNestedValue(result, argWithoutPrefix, true, { pathSeparator });
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
