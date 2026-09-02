import type { PlatformHttpResponseBodyType } from './platform-http-response-body.interface.js';
import type { IPlatformHttpSetCookie } from './platform-http-set-cookie.interface.js';

/**
 * @alpha
 * Framework-neutral immutable HTTP response contract.
 */
export interface IPlatformHttpResponse {
  readonly status: number;
  readonly headers: Readonly<Record<string, string>>;
  readonly body: PlatformHttpResponseBodyType;
  readonly cookies?: readonly IPlatformHttpSetCookie[];
}
