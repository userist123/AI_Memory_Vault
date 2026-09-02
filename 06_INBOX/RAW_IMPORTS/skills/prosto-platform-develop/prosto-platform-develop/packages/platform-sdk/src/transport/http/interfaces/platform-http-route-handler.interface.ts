import type { PlatformHttpMethodType } from '../types/index.js';
import type { IPlatformHttpRequest } from './platform-http-request.interface.js';
import type { IPlatformHttpResponse } from './platform-http-response.interface.js';
import type { IPlatformHttpRouteContext } from './platform-http-route-context.interface.js';

/**
 * @alpha
 * Framework-neutral HTTP route handler contract.
 * Handlers receive a normalized request and typed context, returning a response.
 */
export interface IPlatformHttpRouteHandler<
  TContext extends IPlatformHttpRouteContext = IPlatformHttpRouteContext,
> {
  readonly method: PlatformHttpMethodType;
  readonly route: string;
  handle(
    request: IPlatformHttpRequest,
    context: TContext,
  ): Promise<IPlatformHttpResponse>;
}
