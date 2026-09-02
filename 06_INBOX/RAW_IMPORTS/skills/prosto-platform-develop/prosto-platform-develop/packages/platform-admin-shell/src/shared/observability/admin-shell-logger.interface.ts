/**
 * @alpha
 * Structured logger contract for the admin shell.
 *
 * Follows the IModuleLogger pattern from platform-sdk with additional
 * structured context fields required by ADR-0007:
 * - moduleId, phase, correlationId on every log entry
 * - errorCode for error-level logs
 */
export interface IAdminShellLogger {
  debug(message: string, context?: Readonly<Record<string, unknown>>): void;
  info(message: string, context?: Readonly<Record<string, unknown>>): void;
  warn(message: string, context?: Readonly<Record<string, unknown>>): void;
  error(message: string, context?: Readonly<Record<string, unknown>>): void;
}

/**
 * @alpha
 * Configuration for creating an admin shell logger.
 */
export interface IAdminShellLoggerConfig {
  readonly moduleId?: string;
  readonly level?: 'debug' | 'info' | 'warn' | 'error';
}
