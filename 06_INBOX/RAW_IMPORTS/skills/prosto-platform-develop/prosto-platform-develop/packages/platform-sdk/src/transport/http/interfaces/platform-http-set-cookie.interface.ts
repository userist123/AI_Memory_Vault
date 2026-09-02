/**
 * @alpha
 * Immutable structured response cookie. `expiresAt` is a UTC Unix timestamp
 * in milliseconds. Cookie names, values, and attributes are validated by
 * {@link PlatformHttpSetCookie} before a transport serializes them.
 */
export interface IPlatformHttpSetCookie {
  readonly name: string;
  readonly value: string;
  readonly path?: string;
  readonly domain?: string;
  readonly expiresAt?: number;
  readonly maxAge?: number;
  readonly httpOnly?: boolean;
  readonly secure?: boolean;
  readonly sameSite?: 'strict' | 'lax' | 'none';
}

/**
 * @alpha
 * Input for a structured response cookie. The resulting value object makes a
 * defensive immutable copy and applies cookie-prefix and SameSite rules.
 */
export type PlatformHttpSetCookieInputType = IPlatformHttpSetCookie;
