import type { IPlatformHttpRouteContext } from './platform-http-route-context.interface.js';
import type { IPlatformHttpRouteContextFactoryInput } from './platform-http-route-context-factory-input.interface.js';

/**
 * @alpha
 * Async factory that creates an extended route context from framework-neutral input.
 */
export interface IPlatformHttpRouteContextFactory<
  TContext extends IPlatformHttpRouteContext = IPlatformHttpRouteContext,
> {
  create(input: IPlatformHttpRouteContextFactoryInput): Promise<TContext>;
}
