import type {
  IAdminBffLogger,
  IAdminBffLoggerConfig,
} from './admin-bff-logger.interface.js';
import { ADMIN_BFF_MODULE_ID } from './admin-bff-observability.constants.js';

type LogLevelType = 'debug' | 'info' | 'warn' | 'error';

const LOG_LEVEL_PRIORITY: Record<LogLevelType, number> = {
  debug: 0,
  info: 1,
  warn: 2,
  error: 3,
};

/**
 * @alpha
 * Sensitive key patterns that should be redacted from log context objects.
 */
const SENSITIVE_KEY_PATTERNS = [
  'password',
  'secret',
  'token',
  'apikey',
  'authorization',
  'cookie',
  'credential',
  'subject',
  'roles',
  'permissions',
];

/**
 * @alpha
 * Checks whether a string key matches any sensitive pattern.
 */
function isSensitiveKey(key: string): boolean {
  const lowerKey = key.toLowerCase();
  return SENSITIVE_KEY_PATTERNS.some((pattern) => lowerKey.includes(pattern));
}

/**
 * @alpha
 * Recursively redacts sensitive values in a context object.
 */
function redactContext(
  context: Readonly<Record<string, unknown>> | undefined,
): Record<string, unknown> | undefined {
  if (!context) {
    return undefined;
  }

  const redacted: Record<string, unknown> = {};

  for (const [key, value] of Object.entries(context)) {
    if (isSensitiveKey(key)) {
      redacted[key] = '[REDACTED]';
    } else {
      redacted[key] = redactValue(value);
    }
  }

  return redacted;
}

function redactValue(value: unknown): unknown {
  if (Array.isArray(value)) {
    return value.map((item) => redactValue(item));
  }

  if (typeof value === 'object' && value !== null) {
    return redactContext(value as Record<string, unknown>);
  }

  return value;
}

/**
 * @alpha
 * Console-based structured logger for the admin BFF adapter.
 *
 * Outputs structured JSON logs via console methods with automatic
 * redaction of sensitive context fields and identity PII.
 *
 * All log entries include the moduleId for correlation with platform-wide
 * observability pipelines.
 */
export class ConsoleAdminBffLogger implements IAdminBffLogger {
  private readonly _moduleId: string;
  private readonly _minLevel: number;

  constructor(config: IAdminBffLoggerConfig = {}) {
    this._moduleId = config.moduleId ?? ADMIN_BFF_MODULE_ID;
    this._minLevel = LOG_LEVEL_PRIORITY[config.level ?? 'debug'] ?? 0;
  }

  debug(message: string, context?: Readonly<Record<string, unknown>>): void {
    if (this._minLevel > LOG_LEVEL_PRIORITY.debug) return;

    console.debug(
      `[${this._moduleId}]`,
      this._formatEntry('debug', message, context),
    );
  }

  info(message: string, context?: Readonly<Record<string, unknown>>): void {
    if (this._minLevel > LOG_LEVEL_PRIORITY.info) return;

    console.info(
      `[${this._moduleId}]`,
      this._formatEntry('info', message, context),
    );
  }

  warn(message: string, context?: Readonly<Record<string, unknown>>): void {
    if (this._minLevel > LOG_LEVEL_PRIORITY.warn) return;

    console.warn(
      `[${this._moduleId}]`,
      this._formatEntry('warn', message, context),
    );
  }

  error(message: string, context?: Readonly<Record<string, unknown>>): void {
    if (this._minLevel > LOG_LEVEL_PRIORITY.error) return;

    console.error(
      `[${this._moduleId}]`,
      this._formatEntry('error', message, context),
    );
  }

  private _formatEntry(
    level: LogLevelType,
    message: string,
    context?: Readonly<Record<string, unknown>>,
  ): Record<string, unknown> {
    const entry: Record<string, unknown> = {
      level,
      message,
      moduleId: this._moduleId,
      timestamp: new Date().toISOString(),
    };

    const redacted = redactContext(context);

    if (redacted && Object.keys(redacted).length > 0) {
      entry.context = redacted;
    }

    return entry;
  }
}
