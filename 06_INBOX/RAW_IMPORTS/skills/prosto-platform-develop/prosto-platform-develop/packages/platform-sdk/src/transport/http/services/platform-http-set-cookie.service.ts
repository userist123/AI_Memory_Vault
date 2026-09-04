import type {
  IPlatformHttpSetCookie,
  PlatformHttpSetCookieInputType,
} from '../interfaces/index.js';
import { PlatformHttpError } from '../errors/index.js';

/**
 * @alpha
 * Immutable, validated Set-Cookie instruction. It is the only SDK response
 * contract that may produce a transport Set-Cookie header.
 */
export class PlatformHttpSetCookie implements IPlatformHttpSetCookie {
  readonly name: string;
  readonly value: string;
  readonly path?: string;
  readonly domain?: string;
  readonly expiresAt?: number;
  readonly maxAge?: number;
  readonly httpOnly?: boolean;
  readonly secure?: boolean;
  readonly sameSite?: 'strict' | 'lax' | 'none';

  private readonly TOKEN_PATTERN = /^[!#$%&'*+\-.^_`|~0-9A-Za-z]+$/u;
  private readonly COOKIE_VALUE_PATTERN =
    /^(?:[\x21\x23-\x2B\x2D-\x3A\x3C-\x5B\x5D-\x7E])*$/u;
  private readonly CONTROL_CHARACTER_PATTERN = /\p{Cc}/u;
  private readonly DOMAIN_PATTERN =
    /^(?=.{1,253}$)(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)(?:\.(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?))*$/u;

  constructor(input: PlatformHttpSetCookieInputType) {
    this._validateInput(input);

    this.name = input.name;
    this.value = input.value;
    this.path = input.path;
    this.domain = input.domain?.toLowerCase();
    this.expiresAt = input.expiresAt;
    this.maxAge = input.maxAge;
    this.httpOnly = input.httpOnly;
    this.secure = input.secure;
    this.sameSite = input.sameSite;

    Object.freeze(this);
  }

  private _validateInput(input: PlatformHttpSetCookieInputType): void {
    if (typeof input !== 'object' || input === null || Array.isArray(input)) {
      this._invalid('Cookie input must be an object.');
    }

    if (
      typeof input.name !== 'string' ||
      !this.TOKEN_PATTERN.test(input.name)
    ) {
      this._invalid('Cookie name must be an HTTP token.');
    }

    if (
      typeof input.value !== 'string' ||
      !this.COOKIE_VALUE_PATTERN.test(input.value) ||
      this.CONTROL_CHARACTER_PATTERN.test(input.value)
    ) {
      this._invalid('Cookie value contains invalid characters.');
    }

    if (
      input.path !== undefined &&
      (typeof input.path !== 'string' ||
        input.path.length === 0 ||
        !input.path.startsWith('/') ||
        input.path.includes(';') ||
        this.CONTROL_CHARACTER_PATTERN.test(input.path))
    ) {
      this._invalid('Cookie path is invalid.');
    }

    if (
      input.domain !== undefined &&
      (typeof input.domain !== 'string' ||
        !this.DOMAIN_PATTERN.test(input.domain) ||
        this.CONTROL_CHARACTER_PATTERN.test(input.domain))
    ) {
      this._invalid('Cookie domain is invalid.');
    }

    if (
      input.expiresAt !== undefined &&
      (!Number.isSafeInteger(input.expiresAt) || input.expiresAt < 0)
    ) {
      this._invalid('Cookie expiry must be a non-negative safe integer.');
    }

    if (
      input.maxAge !== undefined &&
      (!Number.isSafeInteger(input.maxAge) || input.maxAge < 0)
    ) {
      this._invalid('Cookie max age must be a non-negative safe integer.');
    }

    if (
      input.sameSite !== undefined &&
      input.sameSite !== 'strict' &&
      input.sameSite !== 'lax' &&
      input.sameSite !== 'none'
    ) {
      this._invalid('Cookie SameSite value is invalid.');
    }

    if (
      (input.httpOnly !== undefined && typeof input.httpOnly !== 'boolean') ||
      (input.secure !== undefined && typeof input.secure !== 'boolean')
    ) {
      this._invalid('Cookie security attributes must be boolean values.');
    }

    if (input.sameSite === 'none' && input.secure !== true) {
      this._invalid('SameSite=None cookies must be Secure.');
    }

    if (input.name.startsWith('__Secure-') && input.secure !== true) {
      this._invalid('__Secure- cookies must be Secure.');
    }

    if (
      input.name.startsWith('__Host-') &&
      (input.secure !== true ||
        input.path !== '/' ||
        input.domain !== undefined)
    ) {
      this._invalid('__Host- cookies must be Secure, Path=/, and host-only.');
    }
  }

  private _invalid(message: string): never {
    throw new PlatformHttpError('INVALID_COOKIE', message);
  }
}
