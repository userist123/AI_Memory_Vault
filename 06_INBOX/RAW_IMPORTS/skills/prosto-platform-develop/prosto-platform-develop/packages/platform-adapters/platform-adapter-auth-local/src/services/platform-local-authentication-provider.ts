import type { IPlatformAuthenticationProvider } from '@prosto/platform-sdk';
import type { IPlatformLocalAuthRuntime } from '@/interfaces/index.js';

/**
 * @alpha
 * Adapts local authentication routes and its opaque-session resolver to the
 * SDK's host-facing provider facade.
 */
export function createPlatformLocalAuthenticationProvider(
  runtime: IPlatformLocalAuthRuntime,
): IPlatformAuthenticationProvider {
  return Object.freeze({
    mode: 'local',
    resolver: runtime.resolver,
    publicRouteRegistrations: runtime.routes,
  });
}
