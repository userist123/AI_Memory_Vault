/**
 * @alpha
 * Options for the SecretsRedactor.
 */
export interface ISecretRedactorOptions {
  /**
   * Whether redaction is active.
   * @default true
   */
  enabled?: boolean;
  /**
   * Key names to redact in `key=value` patterns.
   * @default ['password', 'token', 'secret', 'key', 'apiKey', 'passphrase', 'url', 'connectionString']
   */
  patterns?: string[];
}

/**
 * @alpha
 * Interface for a secrets redactor that strips sensitive data from strings.
 */
export interface ISecretsRedactor {
  /**
   * Redacts sensitive values from a string.
   */
  redact(value: string): string;

  /**
   * Recursively redacts sensitive values from arrays.
   * String values are redacted using the secrets redactor.
   * Array items that are objects are redacted using the secrets redactor.
   */
  redactCollection<T extends unknown[] = unknown[]>(collection?: T): T;

  /**
   * Recursively redacts sensitive values from nested objects.
   * String values are redacted using the secrets redactor.
   * Object keys matching sensitive patterns are replaced with [REDACTED].
   */
  redactObject<T extends Record<string, unknown> = Record<string, unknown>>(
    obj?: T,
  ): T;
}

/**
 * @alpha
 * Redacts known secret patterns from string messages.
 * Uses configurable pattern list from platform settings.
 * Always redacts bearer tokens and basic authorization headers.
 */
export class SecretsRedactor implements ISecretsRedactor {
  private readonly _enabled: boolean;
  private readonly _patterns: string[];
  private readonly _keyValueRegex: RegExp;

  private readonly _builtInRules: readonly {
    pattern: RegExp;
    replacement: string;
  }[] = [
    // Redact basic authorization headers (e.g., "Authorization: Basic dXNlcjpwYXNz...")
    {
      pattern: /(authorization:\s*basic\s+)([^\s,;]+)/gi,
      replacement: '$1[REDACTED]',
    },
    // Redact token (e.g., "Bearer dXNlcjpwYXNz...")
    {
      pattern: /(bearer\s+)([^\s,;]+)/gi,
      replacement: '$1[REDACTED]',
    },
    // Redact connection strings (e.g., "connectionString=Server=...;")
    {
      pattern: /(connection[_-]?string\s*[:=]\s*)([^\s,;]+)/gi,
      replacement: '$1[REDACTED]',
    },
    // Redact private keys (e.g., "privateKey=MIIEvQIBADANBgkqhkiG9w0B...")
    {
      pattern: /(private[_-]?key\s*[:=]\s*)([^\s,;]+)/gi,
      replacement: '$1[REDACTED]',
    },
    // Redact API keys in various formats (e.g., "apiKey=123456789012345678...")
    {
      pattern: /(api[_-]?key\s*[:=]\s*)([^\s,;]+)/gi,
      replacement: '$1[REDACTED]',
    },
    // Redact database URLs (e.g., "databaseUrl=postgres://user:pass@host:5432/db")
    {
      pattern: /(database[_-]?url\s*[:=]\s*)([^\s,;]+)/gi,
      replacement: '$1[REDACTED]',
    },
    // Redact generic URLs because database connection URLs may be reported as url.
    {
      pattern: /(url\s*[:=]\s*)([^\s,;]+)/gi,
      replacement: '$1[REDACTED]',
    },
    // Redact JWT secrets (e.g., "jwtSecret=secret-key-123456789012345...")
    {
      pattern: /(jwt[_-]?secret\s*[:=]\s*)([^\s,;]+)/gi,
      replacement: '$1[REDACTED]',
    },
    // Redact encryption keys (e.g., "encryptionKey=1234567890123456789...")
    {
      pattern: /(encryption[_-]?key\s*[:=]\s*)([^\s,;]+)/gi,
      replacement: '$1[REDACTED]',
    },
  ];

  constructor(options: ISecretRedactorOptions = {}) {
    this._enabled = options.enabled ?? true;
    this._patterns = options.patterns ?? [
      'key',
      'token',
      'secret',
      'password',
      'passphrase',
      'url',
      'connectionString',
    ];

    if (this._patterns.length > 0) {
      this._patterns = this._patterns.map((pattern) =>
        pattern.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'),
      );

      this._keyValueRegex = new RegExp(
        `(${this._patterns.join('|')})=([^\\s]+)`,
        'gi',
      );
    } else {
      this._keyValueRegex = /(?!)/gi; // never matches
    }
  }

  redact(value: string): string {
    if (!this._enabled || !value) {
      return value;
    }

    // Match key=value patterns
    let result = value.replace(this._keyValueRegex, '$1=[REDACTED]');

    // Apply built-in redaction rules
    for (const rule of this._builtInRules) {
      result = result.replace(rule.pattern, rule.replacement);
    }

    return result;
  }

  redactCollection<T extends unknown[] = unknown[]>(collection?: T): T {
    if (!this._enabled || !collection || !Array.isArray(collection)) {
      return collection as unknown as T;
    }

    return collection.map((item) => {
      if (typeof item === 'string') {
        // Redact string values
        return this.redact(item);
      } else if (
        typeof item === 'object' &&
        item !== null &&
        !Array.isArray(item)
      ) {
        // Recursively redact nested objects
        return this.redactObject(item);
      } else if (Array.isArray(item)) {
        // Recursively redact arrays
        return this.redactCollection(item);
      }

      return item;
    }) as T;
  }

  redactObject<T extends object = Record<string, unknown>>(obj?: T): T {
    if (
      !this._enabled ||
      !obj ||
      typeof obj !== 'object' ||
      Array.isArray(obj)
    ) {
      return obj as unknown as T;
    }

    const redacted: Record<string, unknown> = {};

    for (const [key, value] of Object.entries(obj)) {
      // Check if key matches sensitive patterns
      if (this._isSensitiveKey(key)) {
        redacted[key] = '[REDACTED]';
      } else if (typeof value === 'string') {
        // Redact string values
        redacted[key] = this.redact(value);
      } else if (
        typeof value === 'object' &&
        value !== null &&
        !Array.isArray(value)
      ) {
        // Recursively redact nested objects
        redacted[key] = this.redactObject(value);
      } else if (Array.isArray(value)) {
        // Recursively redact arrays
        redacted[key] = this.redactCollection(value);
      } else {
        redacted[key] = value;
      }
    }

    return redacted as T;
  }

  /**
   * Checks if a key matches known sensitive patterns.
   */
  private _isSensitiveKey(key: string): boolean {
    const sensitivePatterns = this._patterns
      .map((pattern) => new RegExp(pattern, 'i'))
      .concat([
        /connection[_-]?string/i,
        /private[_-]?key/i,
        /api[_-]?key/i,
        /database[_-]?url/i,
        /^url$/i,
        /jwt[_-]?secret/i,
        /encryption[_-]?key/i,
      ]);

    return sensitivePatterns.some((pattern) => pattern.test(key));
  }
}
