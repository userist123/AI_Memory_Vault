import type { IPlatformHttpLogger } from './http-logger.interface.js';

/**
 * @alpha
 * Default structured console logger for the HTTP transport.
 */
export class ConsoleHttpLogger implements IPlatformHttpLogger {
  public debug(
    message: string,
    context?: Readonly<Record<string, unknown>>,
  ): void {
    console.debug(message, this._redactContext(context));
  }

  public info(
    message: string,
    context?: Readonly<Record<string, unknown>>,
  ): void {
    console.info(message, this._redactContext(context));
  }

  public warn(
    message: string,
    context?: Readonly<Record<string, unknown>>,
  ): void {
    console.warn(message, this._redactContext(context));
  }

  public error(
    message: string,
    context?: Readonly<Record<string, unknown>>,
  ): void {
    console.error(message, this._redactContext(context));
  }

  private _redactContext(
    context: Readonly<Record<string, unknown>> | undefined,
  ): Readonly<Record<string, unknown>> | undefined {
    return context === undefined ? undefined : this._redactRecord(context);
  }

  private _redactRecord(
    value: Readonly<Record<string, unknown>>,
  ): Readonly<Record<string, unknown>> {
    const redacted: Record<string, unknown> = {};

    for (const [key, nestedValue] of Object.entries(value)) {
      redacted[key] = this._isSensitiveKey(key)
        ? '[REDACTED]'
        : this._redactValue(nestedValue);
    }

    return redacted;
  }

  private _redactValue(value: unknown): unknown {
    if (Array.isArray(value)) {
      return value.map((item) => this._redactValue(item));
    }

    if (typeof value === 'object' && value !== null) {
      return this._redactRecord(value as Readonly<Record<string, unknown>>);
    }

    return value;
  }

  private _isSensitiveKey(key: string): boolean {
    return /authorization|cookie|password|secret|token|credential/iu.test(key);
  }
}
