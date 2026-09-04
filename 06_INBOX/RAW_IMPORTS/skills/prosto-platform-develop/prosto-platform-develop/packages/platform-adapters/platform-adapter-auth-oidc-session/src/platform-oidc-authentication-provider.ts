import {
  type IPlatformAuthenticationProvider,
  type IPlatformHttpRouteRegistration,
  type IPlatformIdentityResolutionRequest,
  type IPlatformRequestIdentityResolver,
  PlatformAnonymousIdentity,
  type PlatformRequestIdentityType,
} from '@prosto/platform-sdk';
import type { IPlatformOidcSessionRuntime } from '@/interfaces/index.js';

/**
 * @alpha
 * Adapts the OIDC browser-session runtime to the host-neutral authentication
 * provider facade. The host owns its session-status response while preserving
 * the existing OIDC login redirect and logout route registrations.
 */
export function createPlatformOidcAuthenticationProvider(
  runtime: IPlatformOidcSessionRuntime,
  bearerResolver?: IPlatformRequestIdentityResolver,
): IPlatformAuthenticationProvider {
  return Object.freeze({
    mode: 'oidc',
    resolver:
      bearerResolver === undefined
        ? runtime.resolver
        : new PlatformOidcAuthenticationProviderResolver(
            runtime.resolver,
            runtime.routes,
            bearerResolver,
          ),
    publicRouteRegistrations: runtime.routes,
  });
}

/** @internal */
class PlatformOidcAuthenticationProviderResolver implements IPlatformRequestIdentityResolver {
  private readonly _publicRoutes: ReadonlySet<string>;

  constructor(
    private readonly _sessionResolver: IPlatformRequestIdentityResolver,
    routes: readonly IPlatformHttpRouteRegistration[],
    private readonly _bearerResolver?: IPlatformRequestIdentityResolver,
  ) {
    this._publicRoutes = new Set(routes.map((route) => route.route));
  }

  resolve(
    request: IPlatformIdentityResolutionRequest,
  ): Promise<PlatformRequestIdentityType> {
    if (
      request.headers.authorization !== undefined &&
      this._bearerResolver !== undefined
    ) {
      return this._bearerResolver.resolve(request);
    }

    if (this._publicRoutes.has(request.path)) {
      return Promise.resolve(new PlatformAnonymousIdentity());
    }

    return this._sessionResolver.resolve(request);
  }
}
