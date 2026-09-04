import type { PlatformHttpMethodType } from '../types/index.js';
import type { IPlatformHttpRouteContextFactoryInput } from './platform-http-route-context-factory-input.interface.js';
import type { IPlatformHttpResponse } from './platform-http-response.interface.js';

/**
 * @alpha
 * Non-generic route registration contract consumed by the HTTP server.
 * Encapsulates a typed handler behind its context factory, hiding the concrete context type.
 */
export interface IPlatformHttpRouteRegistration {
  readonly method: PlatformHttpMethodType;
  readonly route: string;
  execute(
    input: IPlatformHttpRouteContextFactoryInput,
  ): Promise<IPlatformHttpResponse>;
}
