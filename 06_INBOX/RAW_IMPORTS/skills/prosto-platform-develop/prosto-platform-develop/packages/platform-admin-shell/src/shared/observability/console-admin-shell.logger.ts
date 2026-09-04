import type {
  IAdminShellLogger,
  IAdminShellLoggerConfig,
} from './admin-shell-logger.interface';
import { ADMIN_SHELL_MODULE_ID } from './admin-shell-observability.constants';

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
];

function isSensitiveKey(key: string): boolean {
  const lowerKey = key.toLowerCase();
  return SENSITIVE_KEY_PATTERNS.some((pattern) => lowerKey.includes(pattern));
}

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
    } else if (
      typeof value === 'object' &&
      value !== null &&
      !Array.isArray(value)
    ) {
      redacted[key] = redactContext(value as Record<string, unknown>);
    } else {
      redacted[key] = value;
    }
  }

  return redacted;
}

/**
 * @alpha
 * Console-based structured logger for the admin shell.
 *
 * Outputs structured JSON logs via console methods with automatic
 * redaction of sensitive context fields (passwords, tokens, secrets).
 *
 * All log entries include the moduleId for correlation with platform-wide
 * observability pipelines.
 */
export class ConsoleAdminShellLogger implements IAdminShellLogger {
  private readonly _moduleId: string;
  private readonly _minLevel: number;

  constructor(config: IAdminShellLoggerConfig = {}) {
    this._moduleId = config.moduleId ?? ADMIN_SHELL_MODULE_ID;
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
