import { createHash, timingSafeEqual } from 'node:crypto';
import type {
  IPlatformLocalAuthRuntimeConfig,
  IResolvedPlatformLocalAuthRuntimeConfig,
} from '@/interfaces/index.js';
import {
  PLATFORM_LOCAL_AUTH_DEFAULT_ABSOLUTE_TTL_MS,
  PLATFORM_LOCAL_AUTH_DEFAULT_IDLE_TTL_MS,
  PLATFORM_LOCAL_AUTH_DEFAULT_MINIMUM_PASSWORD_LENGTH,
  PLATFORM_LOCAL_AUTH_MAXIMUM_COOKIE_VALUE_LENGTH,
} from '@/constants/index.js';
import { PlatformLocalAuthConfigurationError } from '@/errors/index.js';

/** @internal */
export function hashOpaqueValue(value: string): string {
  return createHash('sha256').update(value, 'utf8').digest('base64url');
}

/** @alpha */
export function normalizePlatformLocalAuthUsername(username: string): string {
  return username.normalize('NFKC').trim().toLocaleLowerCase('en-US');
}

/** @alpha */
export function equalPlatformLocalAuthSecrets(
  left: string,
  right: string,
): boolean {
  const leftBytes = Buffer.from(left, 'utf8');
  const rightBytes = Buffer.from(right, 'utf8');

  return (
    leftBytes.length === rightBytes.length &&
    timingSafeEqual(leftBytes, rightBytes)
  );
}

/** @alpha */
export function parsePlatformLocalAuthCookie(
  headerValues: readonly string[] | undefined,
  name: string,
): string | undefined {
  if (headerValues === undefined) {
    return undefined;
  }

  let value: string | undefined;

  for (const header of headerValues) {
    if (/\p{Cc}/u.test(header)) {
      return undefined;
    }

    for (const part of header.split(';')) {
      const separator = part.indexOf('=');

      if (separator <= 0) {
        continue;
      }

      const cookieName = part.slice(0, separator).trim();
      const cookieValue = part.slice(separator + 1).trim();

      if (cookieName !== name) {
        continue;
      }

      if (
        value !== undefined ||
        cookieValue.length === 0 ||
        cookieValue.length > PLATFORM_LOCAL_AUTH_MAXIMUM_COOKIE_VALUE_LENGTH ||
        /\p{Cc}/u.test(cookieValue)
      ) {
        return undefined;
      }

      value = cookieValue;
    }
  }

  return value;
}

/** @internal */
export function isOpaquePlatformLocalAuthToken(value: string): boolean {
  return /^[A-Za-z0-9_-]{43}$/u.test(value);
}

/** @internal */
export function resolvePlatformLocalAuthConfig(
  input: IPlatformLocalAuthRuntimeConfig,
): IResolvedPlatformLocalAuthRuntimeConfig {
  try {
    const origin = new URL(input.origin);

    if (
      !['http:', 'https:'].includes(origin.protocol) ||
      origin.username ||
      origin.password ||
      origin.pathname !== '/' ||
      origin.search ||
      origin.hash
    ) {
      throw new Error('Unsafe origin.');
    }

    if (
      origin.protocol === 'http:' &&
      !['localhost', '127.0.0.1', '[::1]'].includes(origin.hostname)
    ) {
      throw new Error('Public HTTP is not permitted for local authentication.');
    }

    const sessionIdleTtlMs =
      input.sessionIdleTtlMs ?? PLATFORM_LOCAL_AUTH_DEFAULT_IDLE_TTL_MS;
    const sessionAbsoluteTtlMs =
      input.sessionAbsoluteTtlMs ?? PLATFORM_LOCAL_AUTH_DEFAULT_ABSOLUTE_TTL_MS;
    const minimumPasswordLength =
      input.minimumPasswordLength ??
      PLATFORM_LOCAL_AUTH_DEFAULT_MINIMUM_PASSWORD_LENGTH;

    if (
      !Number.isSafeInteger(sessionIdleTtlMs) ||
      !Number.isSafeInteger(sessionAbsoluteTtlMs) ||
      sessionIdleTtlMs <= 0 ||
      sessionAbsoluteTtlMs <= 0 ||
      !Number.isSafeInteger(minimumPasswordLength) ||
      minimumPasswordLength < 8 ||
      minimumPasswordLength > 1024
    ) {
      throw new Error('Invalid local authentication limits.');
    }

    return Object.freeze({
      origin: origin.origin,
      sessionIdleTtlMs,
      sessionAbsoluteTtlMs,
      minimumPasswordLength,
      secureCookies: input.secureCookies ?? origin.protocol === 'https:',
    });
  } catch {
    throw new PlatformLocalAuthConfigurationError();
  }
}

/** @internal */
export function createPlatformLocalAuthCookie(
  name: string,
  value: string,
  httpOnly: boolean,
  secure: boolean,
  path: '/' | '/admin' = '/',
): {
  readonly name: string;
  readonly value: string;
  readonly path: '/' | '/admin';
  readonly httpOnly: boolean;
  readonly secure: boolean;
  readonly sameSite: 'lax';
} {
  return { name, value, path, httpOnly, secure, sameSite: 'lax' };
}

/** @internal */
export function createExpiredPlatformLocalAuthCookie(
  name: string,
  httpOnly: boolean,
  secure: boolean,
  path: '/' | '/admin' = '/',
): ReturnType<typeof createPlatformLocalAuthCookie> & { readonly maxAge: 0 } {
  return {
    ...createPlatformLocalAuthCookie(name, 'x', httpOnly, secure, path),
    maxAge: 0,
  };
}
