import type { PlatformHttpMethodType } from '../types/index.js';

/**
 * @alpha
 * Narrow immutable input port for identity resolution.
 * Contains only request metadata needed for identity extraction — no body and no already-resolved identity.
 */
export interface IPlatformIdentityResolutionRequest {
  readonly correlationId: string;
  readonly method: PlatformHttpMethodType;
  readonly path: string;
  readonly headers: Readonly<Record<string, readonly string[]>>;
  readonly params: Readonly<Record<string, string>>;
  readonly query: Readonly<Record<string, readonly string[]>>;
}
