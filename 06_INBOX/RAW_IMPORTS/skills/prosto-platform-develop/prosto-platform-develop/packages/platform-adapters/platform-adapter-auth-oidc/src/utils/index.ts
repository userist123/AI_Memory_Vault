import type {
  IPlatformOidcBearerResolverConfig,
  IResolvedBearerConfig,
  PlatformAuthJwtAlgorithmType,
} from '@/interfaces/index.js';
import {
  PlatformDelegatedIdentity,
  PlatformHttpError,
} from '@prosto/platform-sdk';
import {
  ALLOWED_ALGORITHMS,
  BEARER_HEADER_PATTERN,
  DEFAULT_ALLOWED_ALGORITHMS,
  DEFAULT_JWKS_CACHE_MAX_AGE_MS,
  DEFAULT_JWKS_COOLDOWN_DURATION_MS,
  DEFAULT_JWKS_TIMEOUT_MS,
  DEFAULT_PERMISSIONS_CLAIM,
  DEFAULT_ROLES_CLAIM,
  JWT_REJECTION_CODES,
  MAX_BEARER_TOKEN_BYTES,
  MAX_CLAIM_AGGREGATE_BYTES,
  MAX_CLAIM_ENTRIES,
  MAX_CLAIM_ENTRY_BYTES,
  MAX_CLAIM_NAME_BYTES,
  MAX_SUBJECT_BYTES,
} from '@/constants/index.js';
import {
  PlatformAuthConfigurationError,
  type PlatformAuthConfigurationErrorCodeType,
} from '@/errors/index.js';

export function resolveConfig(
  config: IPlatformOidcBearerResolverConfig,
): IResolvedBearerConfig {
  if (typeof config !== 'object' || config === null) {
    throw new PlatformAuthConfigurationError('INVALID_ISSUER');
  }

  validateHttpsUrl(config.issuer, 'INVALID_ISSUER');

  const jwksUri = validateHttpsUrl(config.jwksUri, 'INVALID_JWKS_URI');
  const audiences = validateUniqueTextArray(
    config.audiences,
    'INVALID_AUDIENCES',
  );
  const allowedAlgorithms = validateAllowedAlgorithms(config.allowedAlgorithms);
  const rolesClaim = validateClaimName(
    config.rolesClaim ?? DEFAULT_ROLES_CLAIM,
  );
  const permissionsClaim = validateClaimName(
    config.permissionsClaim ?? DEFAULT_PERMISSIONS_CLAIM,
  );

  if (rolesClaim === permissionsClaim) {
    throw new PlatformAuthConfigurationError('INVALID_CLAIM_NAME');
  }

  return Object.freeze({
    issuer: config.issuer,
    jwksUri,
    audiences,
    allowedAlgorithms,
    rolesClaim,
    permissionsClaim,
    jwksTimeoutMs: validateDuration(
      config.jwksTimeoutMs ?? DEFAULT_JWKS_TIMEOUT_MS,
      DEFAULT_JWKS_TIMEOUT_MS,
    ),
    jwksCacheMaxAgeMs: validateDuration(
      config.jwksCacheMaxAgeMs ?? DEFAULT_JWKS_CACHE_MAX_AGE_MS,
      DEFAULT_JWKS_CACHE_MAX_AGE_MS,
    ),
    jwksCooldownDurationMs: validateDuration(
      config.jwksCooldownDurationMs ?? DEFAULT_JWKS_COOLDOWN_DURATION_MS,
      DEFAULT_JWKS_COOLDOWN_DURATION_MS,
    ),
  });
}

export function fetchJwksWithoutRedirects(
  input: RequestInfo | URL,
  init?: RequestInit,
): Promise<Response> {
  return globalThis.fetch(input, { ...init, redirect: 'error' });
}

export function validateHttpsUrl(
  value: string,
  code: PlatformAuthConfigurationErrorCodeType,
): URL {
  if (typeof value !== 'string' || value.trim() !== value || !value.length) {
    throw new PlatformAuthConfigurationError(code);
  }

  let url: URL;
  try {
    url = new URL(value);
  } catch {
    throw new PlatformAuthConfigurationError(code);
  }

  if (
    url.protocol !== 'https:' ||
    url.username.length > 0 ||
    url.password.length > 0 ||
    url.hash.length > 0
  ) {
    throw new PlatformAuthConfigurationError(code);
  }

  return url;
}

export function validateUniqueTextArray(
  values: readonly string[],
  code: PlatformAuthConfigurationErrorCodeType,
): readonly string[] {
  if (!Array.isArray(values) || values.length === 0) {
    throw new PlatformAuthConfigurationError(code);
  }

  const unique = new Set<string>();
  for (const value of values) {
    if (
      typeof value !== 'string' ||
      !value.trim().length ||
      value.trim() !== value ||
      containsControlCharacters(value) ||
      unique.has(value)
    ) {
      throw new PlatformAuthConfigurationError(code);
    }

    unique.add(value);
  }

  return Object.freeze([...unique]);
}

export function validateAllowedAlgorithms(
  values: readonly PlatformAuthJwtAlgorithmType[] | undefined,
): readonly PlatformAuthJwtAlgorithmType[] {
  const algorithms = values ?? DEFAULT_ALLOWED_ALGORITHMS;
  if (!Array.isArray(algorithms) || algorithms.length === 0) {
    throw new PlatformAuthConfigurationError('INVALID_ALLOWED_ALGORITHMS');
  }

  const unique = new Set<PlatformAuthJwtAlgorithmType>();
  for (const algorithm of algorithms) {
    if (!ALLOWED_ALGORITHMS.has(algorithm) || unique.has(algorithm)) {
      throw new PlatformAuthConfigurationError('INVALID_ALLOWED_ALGORITHMS');
    }

    unique.add(algorithm);
  }

  return Object.freeze([...unique]);
}

export function validateClaimName(value: string): string {
  if (
    typeof value !== 'string' ||
    !value.trim().length ||
    value.trim() !== value ||
    containsControlCharacters(value) ||
    byteLength(value) > MAX_CLAIM_NAME_BYTES
  ) {
    throw new PlatformAuthConfigurationError('INVALID_CLAIM_NAME');
  }

  return value;
}

export function validateDuration(value: number, maximum: number): number {
  if (!Number.isSafeInteger(value) || value <= 0 || value > maximum) {
    throw new PlatformAuthConfigurationError('INVALID_JWKS_DURATION');
  }

  return value;
}

export function extractBearerToken(
  authorizationHeaders: readonly string[] | undefined,
): string | undefined {
  if (authorizationHeaders === undefined) {
    return undefined;
  }

  if (authorizationHeaders.length !== 1) {
    throw unauthenticated();
  }

  const match = BEARER_HEADER_PATTERN.exec(authorizationHeaders[0] ?? '');
  const token = match?.[1];

  if (token === undefined || byteLength(token) > MAX_BEARER_TOKEN_BYTES) {
    throw unauthenticated();
  }

  return token;
}

export function mapDelegatedIdentity(
  payload: Readonly<Record<string, unknown>>,
  config: IResolvedBearerConfig,
): PlatformDelegatedIdentity {
  const subjectId = payload.sub;

  if (
    typeof subjectId !== 'string' ||
    !subjectId.length ||
    !subjectId.trim().length ||
    containsControlCharacters(subjectId) ||
    byteLength(subjectId) > MAX_SUBJECT_BYTES
  ) {
    throw unauthenticated();
  }

  return new PlatformDelegatedIdentity({
    subjectId,
    roles: mapClaimArray(payload[config.rolesClaim]),
    permissions: mapClaimArray(payload[config.permissionsClaim]),
  });
}

export function mapClaimArray(value: unknown): readonly string[] {
  if (value === undefined) {
    return [];
  }

  if (!Array.isArray(value) || value.length > MAX_CLAIM_ENTRIES) {
    throw unauthenticated();
  }

  let aggregateBytes = 0;
  const unique = new Set<string>();
  for (const entry of value) {
    if (typeof entry !== 'string') {
      throw unauthenticated();
    }

    const normalized = entry.trim();
    const entryBytes = byteLength(normalized);
    if (
      !normalized.length ||
      containsControlCharacters(normalized) ||
      entryBytes > MAX_CLAIM_ENTRY_BYTES ||
      unique.has(normalized)
    ) {
      throw unauthenticated();
    }

    unique.add(normalized);

    aggregateBytes += entryBytes;
    if (aggregateBytes > MAX_CLAIM_AGGREGATE_BYTES) {
      throw unauthenticated();
    }
  }

  return [...unique];
}

export function toResolutionError(error: unknown): PlatformHttpError {
  if (error instanceof PlatformHttpError) {
    return error;
  }

  const errorCode = getErrorCode(error);

  if (errorCode !== undefined && JWT_REJECTION_CODES.has(errorCode)) {
    return unauthenticated();
  }

  return new PlatformHttpError(
    'IDENTITY_RESOLUTION_UNAVAILABLE',
    'Bearer identity resolution is temporarily unavailable.',
  );
}

export function getErrorCode(error: unknown): string | undefined {
  if (
    typeof error !== 'object' ||
    error === null ||
    !('code' in error) ||
    typeof error.code !== 'string'
  ) {
    return undefined;
  }

  return error.code;
}

export function unauthenticated(): PlatformHttpError {
  return new PlatformHttpError(
    'HTTP_UNAUTHENTICATED',
    'Bearer credential is invalid.',
  );
}

export function byteLength(value: string): number {
  return new TextEncoder().encode(value).byteLength;
}

export function containsControlCharacters(value: string): boolean {
  for (const character of value) {
    const codePoint = character.codePointAt(0);
    if (
      codePoint !== undefined &&
      (codePoint <= 0x1f || (codePoint >= 0x7f && codePoint <= 0x9f))
    ) {
      return true;
    }
  }

  return false;
}
