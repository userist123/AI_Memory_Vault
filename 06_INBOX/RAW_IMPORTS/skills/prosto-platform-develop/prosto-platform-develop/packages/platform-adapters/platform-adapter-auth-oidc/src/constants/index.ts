import type { PlatformAuthJwtAlgorithmType } from '@/interfaces/index.js';

export const DEFAULT_ALLOWED_ALGORITHMS: readonly PlatformAuthJwtAlgorithmType[] =
  ['RS256'];
export const DEFAULT_ROLES_CLAIM = 'roles';
export const DEFAULT_PERMISSIONS_CLAIM = 'permissions';
export const DEFAULT_JWKS_TIMEOUT_MS = 5_000;
export const DEFAULT_JWKS_CACHE_MAX_AGE_MS = 10 * 60 * 1_000;
export const DEFAULT_JWKS_COOLDOWN_DURATION_MS = 30 * 1_000;
export const MAX_BEARER_TOKEN_BYTES = 12 * 1_024;
export const MAX_SUBJECT_BYTES = 255;
export const MAX_CLAIM_NAME_BYTES = 255;
export const MAX_CLAIM_ENTRY_BYTES = 128;
export const MAX_CLAIM_ENTRIES = 100;
export const MAX_CLAIM_AGGREGATE_BYTES = 8 * 1_024;
export const BEARER_HEADER_PATTERN = /^Bearer ([^\s]+)$/iu;
export const ALLOWED_ALGORITHMS = new Set<PlatformAuthJwtAlgorithmType>([
  'RS256',
  'PS256',
  'ES256',
]);
export const JWT_REJECTION_CODES = new Set([
  'ERR_JOSE_ALG_NOT_ALLOWED',
  'ERR_JWS_INVALID',
  'ERR_JWS_SIGNATURE_VERIFICATION_FAILED',
  'ERR_JWT_CLAIM_VALIDATION_FAILED',
  'ERR_JWT_EXPIRED',
  'ERR_JWT_INVALID',
  'ERR_JWKS_NO_MATCHING_KEY',
]);
