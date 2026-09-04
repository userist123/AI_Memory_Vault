/**
 * Checks if a value is a plain object (not an array or null).
 */
export function isPlainObject(
  value: unknown,
): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export function freezeRecord(
  record: Record<string, string>,
): Readonly<Record<string, string>> {
  return Object.freeze({ ...record });
}

export function freezeRecordOfArrays(
  record: Record<string, readonly string[]>,
): Readonly<Record<string, readonly string[]>> {
  const copy: Record<string, readonly string[]> = {};

  for (const [key, value] of Object.entries(record)) {
    copy[key] = Object.freeze([...value]);
  }

  return Object.freeze(copy);
}

export function freezeStringArray(arr: readonly string[]): readonly string[] {
  return Object.freeze([...arr]);
}

/**
 * Resolves a nested value from a data object using a dot-separated key.
 */
export function resolveNestedValue<T>(
  data: Record<string, unknown>,
  key = '',
): T | undefined {
  return key.split('.').reduce<unknown>((obj, part) => {
    if (obj && typeof obj === 'object' && part in obj) {
      return (obj as Record<string, unknown>)[part];
    }

    return undefined;
  }, data) as T;
}

/**
 * Sets a nested value in a data object using a custom-separated path.
 */
export function setNestedValue(
  target: Record<string, unknown>,
  path: string,
  value: unknown,
  options: {
    /** @default '.' */
    pathSeparator?: string;
  } = {},
): void {
  const { pathSeparator = '.' } = options;
  const keys = path.split(pathSeparator);
  let current: Record<string, unknown> = target;

  for (let i = 0, l = keys.length - 1; i < l; i++) {
    const key = keys[i] as string;

    if (!isPlainObject(current[key])) {
      current[key] = {};
    }

    current = current[key] as Record<string, unknown>;
  }

  const lastKey = keys[keys.length - 1] as string;

  current[lastKey] = value;
}

/**
 * Collects all keys in a data object, optionally with a prefix.
 */
export function collectKeys(
  data: Record<string, unknown>,
  prefix = '',
): string[] {
  const keys: string[] = [];

  for (const [key, value] of Object.entries(data)) {
    const fullKey = prefix ? `${prefix}.${key}` : key;

    keys.push(fullKey);

    if (value && typeof value === 'object' && !Array.isArray(value)) {
      keys.push(...collectKeys(value as Record<string, unknown>, fullKey));
    }
  }

  return keys;
}

/**
 * Config utility contract.
 */
export interface IConfigUtils {
  get<T = unknown>(key: string): T;
  getValue<T = unknown>(key: string, defaultValue: T): T;
  getSection<T = Record<string, unknown> | undefined>(key: string): T;
  has(key: string): boolean;
  keys(key?: string): string[];
}

/**
 * Creates a config object with utility methods for accessing nested values.
 * @example
 *  const config = createConfigObject({ a: { b: { c: 1 } } });
 *
 *  config.get('a.b.c'); // 1
 *  config.getValue('a.b.d', 2); // 2
 *  config.getSection('a'); // { b: { c: 1 } }
 *  config.has('a.b.c'); // true
 *  config.keys(); // ['a.b.c', 'a']
 *  config.keys('a'); // ['b.c']
 */
export function createConfigObject<
  TConfig extends object = Record<string, unknown>,
>(data: TConfig): TConfig & IConfigUtils {
  return Object.setPrototypeOf(data, {
    get<T = unknown>(key = ''): T {
      if (key === '') return this as T;

      return resolveNestedValue(
        this as unknown as Record<string, unknown>,
        key,
      ) as T;
    },

    getValue<T>(key: string, defaultValue: T): T {
      return this.get<T>(key) ?? defaultValue;
    },

    getSection<T = Record<string, unknown> | undefined>(key: string): T {
      return this.get<T>(key);
    },

    has(key: string): boolean {
      return (
        resolveNestedValue(this as unknown as Record<string, unknown>, key) !==
        undefined
      );
    },

    keys(key = ''): string[] {
      const value = key ? this.get(key) : this;

      if (!value || typeof value !== 'object') {
        return [];
      }

      return collectKeys(value as Record<string, unknown>);
    },
  } as IConfigUtils);
}
