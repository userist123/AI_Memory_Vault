import type { PlatformHttpServerStateType } from './http-server.interfaces.js';

/**
 * @alpha
 * Lifecycle errors raised before HTTP request processing begins.
 */
export type PlatformHttpServerLifecycleErrorCodeType =
  | 'INVALID_LIFECYCLE_TRANSITION'
  | 'INVALID_SERVER_CONFIGURATION'
  | 'ROUTE_REGISTRATION_FAILED';

/**
 * @alpha
 * Typed lifecycle error that does not expose Fastify or network errors.
 */
export class PlatformHttpServerLifecycleError extends Error {
  public readonly code: PlatformHttpServerLifecycleErrorCodeType;
  public readonly state: PlatformHttpServerStateType;

  public constructor(
    code: PlatformHttpServerLifecycleErrorCodeType,
    message: string,
    state: PlatformHttpServerStateType,
  ) {
    super(message);
    this.name = 'PlatformHttpServerLifecycleError';
    this.code = code;
    this.state = state;
  }
}

/**
 * @internal
 * Error raised by the adapter-owned finite body parsers. Its public response
 * mapping is intentionally kept in the HTTP server rather than leaking
 * Fastify parser errors to callers.
 */
export class PlatformHttpBodyParseError extends Error {
  public constructor(
    public readonly code: 'INVALID_REQUEST_BODY',
    message: string,
  ) {
    super(message);
    this.name = 'PlatformHttpBodyParseError';
  }
}
