import type { IPlatformModuleLogger } from '@prosto/platform-sdk';
import { type ISecretsRedactor, SecretsRedactor } from '@/security/index.js';

/**
 * @alpha
 * Console-based implementation of the module logger with built-in secret redaction.
 * All log messages and context values are automatically redacted to prevent
 * sensitive data leakage in production and staging environments.
 */
export class ConsoleModuleLogger implements IPlatformModuleLogger {
  constructor(
    private readonly _moduleId: string,
    private readonly _secretsRedactor: ISecretsRedactor = new SecretsRedactor(),
  ) {}

  debug(message: string, context?: Record<string, unknown>): void {
    const redactedMessage = this._secretsRedactor.redact(message);
    const redactedContext = this._secretsRedactor.redactObject(context);

    console.debug(
      `[Module:${this._moduleId}] ${redactedMessage}`,
      redactedContext ?? {},
    );
  }

  info(message: string, context?: Record<string, unknown>): void {
    const redactedMessage = this._secretsRedactor.redact(message);
    const redactedContext = this._secretsRedactor.redactObject(context);

    console.info(
      `[Module:${this._moduleId}] ${redactedMessage}`,
      redactedContext ?? {},
    );
  }

  warn(message: string, context?: Record<string, unknown>): void {
    const redactedMessage = this._secretsRedactor.redact(message);
    const redactedContext = this._secretsRedactor.redactObject(context);

    console.warn(
      `[Module:${this._moduleId}] ${redactedMessage}`,
      redactedContext ?? {},
    );
  }

  error(message: string, context?: Record<string, unknown>): void {
    const redactedMessage = this._secretsRedactor.redact(message);
    const redactedContext = this._secretsRedactor.redactObject(context);

    console.error(
      `[Module:${this._moduleId}] ${redactedMessage}`,
      redactedContext ?? {},
    );
  }
}
