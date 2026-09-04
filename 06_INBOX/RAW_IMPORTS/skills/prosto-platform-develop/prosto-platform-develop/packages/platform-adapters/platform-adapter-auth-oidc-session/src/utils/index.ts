import type {
  IPlatformOidcSessionClock,
  IPlatformOidcSessionRecord,
  IPlatformOidcSessionRuntimeConfig,
  IResolvedConfig,
} from '@/interfaces/index.js';
import { createHash, randomBytes, timingSafeEqual } from 'node:crypto';
import {
  PlatformDelegatedIdentity,
  PlatformHttpError,
  PlatformHttpResponse,
} from '@prosto/platform-sdk';
import {
  MAX_TOKEN_EXPIRY_SECONDS,
  MIN_TOKEN_EXPIRY_SECONDS,
  OIDC_TRANSACTION_TTL_SECONDS,
  SESSION_COOKIE_PREFIX,
  SESSION_IDLE_TTL_MS,
  TRANSACTION_COOKIE_NAME,
} from '@/constants/index.js';
import { PlatformOidcSessionConfigurationError } from '@/errors/index.js';

export const systemClock: IPlatformOidcSessionClock = Object.freeze({
  now: (): number => Date.now(),
  sleep: async (milliseconds: number): Promise<void> =>
    new Promise((resolve) => setTimeout(resolve, milliseconds)),
});

export function randomBase64Url(bytes: number): string {
  return randomBytes(bytes).toString('base64url');
}

export function hashOpaqueValue(value: string): string {
  return createHash('sha256').update(value, 'utf8').digest('base64url');
}

export function equalSecrets(left: string, right: string): boolean {
  const leftValue = Buffer.from(left, 'utf8');
  const rightValue = Buffer.from(right, 'utf8');

  return (
    leftValue.length === rightValue.length &&
    timingSafeEqual(leftValue, rightValue)
  );
}

export function isSafeText(value: string, maxBytes: number): boolean {
  return (
    value.trim().length > 0 &&
    !/\p{Cc}/u.test(value) &&
    Buffer.byteLength(value, 'utf8') <= maxBytes
  );
}

export function isOpaqueId(value: string): boolean {
  return /^[A-Za-z0-9_-]{43}$/u.test(value);
}

export function isPkceVerifier(value: string): boolean {
  return /^[A-Za-z0-9\-._~]{43,128}$/u.test(value);
}

export function parseSingleCookie(
  headerValues: readonly string[] | undefined,
  name: string,
): string | undefined {
  if (headerValues === undefined) {
    return undefined;
  }

  let result: string | undefined;
  for (const header of headerValues) {
    if (/\p{Cc}/u.test(header)) {
      throw new Error('Invalid cookie header.');
    }

    for (const part of header.split(';')) {
      const separator = part.indexOf('=');
      if (separator <= 0) {
        continue;
      }
      const cookieName = part.slice(0, separator).trim();
      const value = part.slice(separator + 1).trim();
      if (cookieName !== name) {
        continue;
      }
      if (result !== undefined || value.length === 0 || /\p{Cc}/u.test(value)) {
        throw new Error('Duplicate or invalid cookie.');
      }
      result = value;
    }
  }

  return result;
}

export function resolveConfig(
  input: IPlatformOidcSessionRuntimeConfig,
): IResolvedConfig {
  try {
    const issuer = validateUrl(input.issuer);
    const jwksUri = validateUrl(input.jwksUri);
    const authorizationEndpoint = validateUrl(input.authorizationEndpoint);
    const tokenEndpoint = validateUrl(input.tokenEndpoint);
    const revocationEndpoint = validateUrl(input.revocationEndpoint);
    const redirectUri = validateUrl(input.redirectUri);
    const scopes = validateStringArray(input.scopes, 100, 1024);
    const audiences = validateStringArray(input.audiences, 100, 8 * 1024);
    if (!scopes.includes('openid') || !scopes.includes('offline_access')) {
      throw new Error('Required scopes are missing.');
    }
    if (
      !isSafeText(input.clientId, 255) ||
      !isSafeText(input.clientSecret, 16 * 1024)
    ) {
      throw new Error('Invalid client credentials.');
    }
    const allowedAlgorithms = input.allowedAlgorithms ?? ['RS256'];
    if (
      !allowedAlgorithms.length ||
      new Set(allowedAlgorithms).size !== allowedAlgorithms.length ||
      !allowedAlgorithms.every((algorithm) =>
        ['RS256', 'PS256', 'ES256'].includes(algorithm),
      )
    ) {
      throw new Error('Invalid algorithms.');
    }
    const cookieVersion = input.cookieVersion ?? 1;
    if (!Number.isSafeInteger(cookieVersion) || cookieVersion < 1) {
      throw new Error('Invalid cookie version.');
    }
    if (
      input.resource !== undefined &&
      (!isSafeText(input.resource, 255) || !audiences.includes(input.resource))
    ) {
      throw new Error('Invalid resource.');
    }
    return Object.freeze({
      ...input,
      issuer,
      jwksUri,
      authorizationEndpoint,
      tokenEndpoint,
      revocationEndpoint,
      redirectUri,
      scopes: Object.freeze(scopes),
      audiences: Object.freeze(audiences),
      allowedAlgorithms: Object.freeze([...allowedAlgorithms]),
      cookieVersion,
      sessionCookieName: `${SESSION_COOKIE_PREFIX}${cookieVersion.toString()}`,
    });
  } catch {
    throw new PlatformOidcSessionConfigurationError();
  }
}

export function validateUrl(value: string): string {
  const url = new URL(value);
  if (url.protocol !== 'https:' || url.username || url.password || url.hash) {
    throw new Error('Unsafe OIDC endpoint.');
  }
  return url.href;
}

export function validateStringArray(
  value: readonly string[],
  maxItems: number,
  maxBytes: number,
): string[] {
  if (!Array.isArray(value) || value.length === 0 || value.length > maxItems) {
    throw new Error('Invalid string array.');
  }
  const normalized = value.map((entry) => {
    if (typeof entry !== 'string' || !isSafeText(entry, 128)) {
      throw new Error('Invalid string value.');
    }
    return entry.trim();
  });
  if (
    new Set(normalized).size !== normalized.length ||
    Buffer.byteLength(normalized.join(','), 'utf8') > maxBytes
  ) {
    throw new Error('Invalid string array.');
  }
  return normalized;
}

export function hasExpired(
  session: IPlatformOidcSessionRecord,
  now: number,
): boolean {
  return (
    now >= session.absoluteExpiresAt ||
    now >= session.lastSeenAt + SESSION_IDLE_TTL_MS
  );
}

export function isSessionRecord(value: IPlatformOidcSessionRecord): boolean {
  try {
    if (
      !isSafeText(value.subjectId, 255) ||
      !isOpaqueId(value.sessionIdHash) ||
      !Number.isFinite(value.createdAt) ||
      !Number.isFinite(value.lastSeenAt) ||
      !Number.isFinite(value.absoluteExpiresAt) ||
      !Number.isFinite(value.accessExpiresAt) ||
      value.createdAt > value.lastSeenAt ||
      value.lastSeenAt > value.absoluteExpiresAt
    ) {
      return false;
    }
    validateStringArray(value.roles, 100, 8 * 1024);
    validateStringArray(value.permissions, 100, 8 * 1024);
    return true;
  } catch {
    return false;
  }
}

export function isTransactionRecord(value: {
  readonly transactionIdHash: string;
  readonly stateHash: string;
  readonly nonce: string;
  readonly expiresAt: number;
}): boolean {
  return (
    isOpaqueId(value.transactionIdHash) &&
    isOpaqueId(value.stateHash) &&
    isOpaqueId(value.nonce) &&
    Number.isFinite(value.expiresAt)
  );
}

export function identityFromSession(
  session: IPlatformOidcSessionRecord,
): PlatformDelegatedIdentity {
  return new PlatformDelegatedIdentity({
    subjectId: session.subjectId,
    roles: session.roles,
    permissions: session.permissions,
  });
}

export function validateTokenSet(value: {
  readonly accessToken: string;
  readonly refreshToken: string;
  readonly expiresIn: number;
}): void {
  if (
    !isSafeText(value.accessToken, 16 * 1024) ||
    !isSafeText(value.refreshToken, 16 * 1024) ||
    !Number.isInteger(value.expiresIn) ||
    value.expiresIn < MIN_TOKEN_EXPIRY_SECONDS ||
    value.expiresIn > MAX_TOKEN_EXPIRY_SECONDS
  ) {
    throw new Error('OIDC token response is invalid.');
  }
}

export function parseCallback(
  query: Readonly<Record<string, readonly string[]>>,
):
  | { readonly kind: 'success'; readonly code: string; readonly state: string }
  | { readonly kind: 'denial'; readonly state: string } {
  const allowed = new Set([
    'code',
    'state',
    'error',
    'error_description',
    'error_uri',
  ]);
  if (Object.keys(query).some((key) => !allowed.has(key))) {
    throw new Error('Unexpected callback parameter.');
  }
  const state = singleQueryValue(query.state);
  const code = optionalQueryValue(query.code);
  const error = optionalQueryValue(query.error);
  optionalQueryValue(query.error_description);
  optionalQueryValue(query.error_uri);
  if (code !== undefined && error === undefined) {
    return { kind: 'success', code, state };
  }
  if (code === undefined && error !== undefined) {
    return { kind: 'denial', state };
  }
  throw new Error('Invalid callback outcome.');
}

export function singleQueryValue(
  values: readonly string[] | undefined,
): string {
  const value = optionalQueryValue(values);
  if (value === undefined) {
    throw new Error('Missing callback parameter.');
  }
  return value;
}

export function optionalQueryValue(
  values: readonly string[] | undefined,
): string | undefined {
  if (values === undefined) {
    return undefined;
  }
  if (values.length !== 1 || !isSafeText(values[0] ?? '', 16 * 1024)) {
    throw new Error('Invalid callback parameter.');
  }
  return values[0];
}

export function transactionCookie(value: string): {
  readonly name: string;
  readonly value: string;
  readonly path: string;
  readonly maxAge: number;
  readonly httpOnly: boolean;
  readonly secure: boolean;
  readonly sameSite: 'lax';
} {
  return {
    name: TRANSACTION_COOKIE_NAME,
    value,
    path: '/',
    maxAge: OIDC_TRANSACTION_TTL_SECONDS,
    httpOnly: true,
    secure: true,
    sameSite: 'lax',
  };
}

export function sessionCookie(
  config: IResolvedConfig,
  value: string,
): {
  readonly name: string;
  readonly value: string;
  readonly path: string;
  readonly httpOnly: boolean;
  readonly secure: boolean;
  readonly sameSite: 'strict';
} {
  return {
    name: config.sessionCookieName,
    value,
    path: '/',
    httpOnly: true,
    secure: true,
    sameSite: 'strict',
  };
}

export function clearTransactionCookie(): ReturnType<typeof transactionCookie> {
  return { ...transactionCookie('x'), maxAge: 0 };
}

export function clearSessionCookie(
  config: IResolvedConfig,
): ReturnType<typeof sessionCookie> & { readonly maxAge: number } {
  return { ...sessionCookie(config, 'x'), maxAge: 0 };
}

export function redirect(
  location: string,
  cookies: readonly {
    readonly name: string;
    readonly value: string;
    readonly path: string;
    readonly httpOnly: boolean;
    readonly secure: boolean;
    readonly sameSite: 'strict' | 'lax';
    readonly maxAge?: number;
  }[],
): PlatformHttpResponse {
  return new PlatformHttpResponse({
    status: 302,
    headers: { location },
    cookies,
  });
}

export function failedCallbackResponse(): PlatformHttpResponse {
  return redirect('/?auth=failed', [clearTransactionCookie()]);
}

export function unauthenticated(): PlatformHttpError {
  return new PlatformHttpError(
    'HTTP_UNAUTHENTICATED',
    'Session authentication rejected.',
  );
}

export function unavailable(): PlatformHttpError {
  return new PlatformHttpError(
    'IDENTITY_RESOLUTION_UNAVAILABLE',
    'Session identity resolution is temporarily unavailable.',
  );
}
