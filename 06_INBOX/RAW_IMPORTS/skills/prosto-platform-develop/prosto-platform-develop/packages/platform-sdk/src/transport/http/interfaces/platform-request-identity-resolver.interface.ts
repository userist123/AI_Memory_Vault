import type { IPlatformIdentityResolutionRequest } from './platform-identity-resolution-request.interface.js';
import type { PlatformRequestIdentityType } from './platform-request-identity.interface.js';

/**
 * @alpha
 * Async resolver that builds a {@link PlatformRequestIdentityType} from request metadata only.
 * Receives no Fastify/Node objects, body data, or already-resolved identity.
 */
export interface IPlatformRequestIdentityResolver {
  resolve(
    request: IPlatformIdentityResolutionRequest,
  ): Promise<PlatformRequestIdentityType>;
}
