import type { PlatformHttpMethodType } from '../types/index.js';
import type { PlatformHttpRequestBodyType } from './platform-http-request-body.interface.js';
import type { PlatformRequestIdentityType } from './platform-request-identity.interface.js';

/**
 * @alpha
 * Framework-neutral immutable representation of a normalized incoming HTTP request.
 */
export interface IPlatformHttpRequest {
  readonly method: PlatformHttpMethodType;
  readonly path: string;
  readonly params: Readonly<Record<string, string>>;
  readonly query: Readonly<Record<string, readonly string[]>>;
  readonly headers: Readonly<Record<string, readonly string[]>>;
  readonly body: PlatformHttpRequestBodyType;
  readonly correlationId: string;
  readonly identity: PlatformRequestIdentityType;
}
