/**
 * @alpha
 * Structured logger used by the HTTP transport. Implementations must redact
 * sensitive request metadata before emitting it.
 */
export interface IPlatformHttpLogger {
  debug(message: string, context?: Readonly<Record<string, unknown>>): void;
  info(message: string, context?: Readonly<Record<string, unknown>>): void;
  warn(message: string, context?: Readonly<Record<string, unknown>>): void;
  error(message: string, context?: Readonly<Record<string, unknown>>): void;
}
