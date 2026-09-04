import type { IPlatformHttpRequest } from './platform-http-request.interface.js';
import type { IPlatformHttpRouteContext } from './platform-http-route-context.interface.js';

/**
 * @alpha
 * Immutable input for a route context factory.
 * Carries the normalized request and a base context with correlation ID, identity, and signal.
 */
export interface IPlatformHttpRouteContextFactoryInput {
  readonly request: IPlatformHttpRequest;
  readonly baseContext: IPlatformHttpRouteContext;
}
