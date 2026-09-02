import type { PlatformRequestIdentityType } from './platform-request-identity.interface.js';

/**
 * @alpha
 * Framework-neutral route handler context.
 * Contains correlation ID, resolved identity, and an `AbortSignal` that is
 * cancelled on client disconnect and forced graceful shutdown.
 */
export interface IPlatformHttpRouteContext {
  readonly correlationId: string;
  readonly identity: PlatformRequestIdentityType;
  readonly signal: AbortSignal;
}
