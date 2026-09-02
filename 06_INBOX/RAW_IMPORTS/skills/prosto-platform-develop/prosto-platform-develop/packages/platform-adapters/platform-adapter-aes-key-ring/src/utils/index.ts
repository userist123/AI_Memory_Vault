import type { IValidatedAad, IValidatedKeyRing } from '@/interfaces/index.js';
import { PlatformSecretCipherError } from '@prosto/platform-sdk';
import { createSecretKey, type KeyObject } from 'node:crypto';
import {
  AES_256_KEY_BYTES,
  AES_GCM_NONCE_BYTES,
  AES_GCM_TAG_BYTES,
  BASE64URL_PATTERN,
  DECRYPTION_FAILED_MESSAGE,
  INVALID_INPUT_MESSAGE,
  KEY_ID_PATTERN,
  KEY_UNAVAILABLE_MESSAGE,
  MAX_CIPHERTEXT_BYTES,
  MAX_KEY_COUNT,
  MAX_KEY_ID_LENGTH,
  MAX_REFRESH_TOKEN_BYTES,
  PKCE_VERIFIER_PATTERN,
  RECORD_HASH_BYTES,
  RECORD_HASH_LENGTH,
} from '@/constants/index.js';
import { PlatformAesKeyRingConfigurationError } from '@/errors/index.js';

export function validateKeyRing(value: unknown): IValidatedKeyRing {
  if (!isRecord(value) || !hasOnlyKeys(value, ['activeKeyId', 'keys'])) {
    throw invalidKeyRing();
  }

  const activeKeyId = value.activeKeyId;
  const keyEntries = value.keys;

  if (!isValidKeyId(activeKeyId) || !Array.isArray(keyEntries)) {
    throw invalidKeyRing();
  }

  if (keyEntries.length === 0 || keyEntries.length > MAX_KEY_COUNT) {
    throw invalidKeyRing();
  }

  const keys = new Map<string, KeyObject>();

  for (const entry of keyEntries) {
    if (!isRecord(entry) || !hasOnlyKeys(entry, ['id', 'key'])) {
      throw invalidKeyRing();
    }

    if (!isValidKeyId(entry.id) || typeof entry.key !== 'string') {
      throw invalidKeyRing();
    }

    const keyBytes = decodeCanonicalBase64Url(entry.key, AES_256_KEY_BYTES);
    if (keyBytes === undefined || keys.has(entry.id)) {
      throw invalidKeyRing();
    }

    const keyMaterial = Buffer.from(keyBytes);

    try {
      keys.set(entry.id, createSecretKey(keyMaterial));
    } finally {
      keyMaterial.fill(0);
    }
  }

  if (!keys.has(activeKeyId)) {
    throw invalidKeyRing();
  }

  return Object.freeze({ activeKeyId, keys });
}

export function validateAad(value: unknown): IValidatedAad {
  if (
    !isRecord(value) ||
    !hasOnlyKeys(value, ['schemaVersion', 'recordHash', 'purpose'])
  ) {
    throw invalidInput();
  }

  if (
    typeof value.schemaVersion !== 'number' ||
    !Number.isSafeInteger(value.schemaVersion) ||
    value.schemaVersion < 1 ||
    (value.purpose !== 'refresh-token' && value.purpose !== 'pkce-verifier')
  ) {
    throw invalidInput();
  }

  const recordHash = decodeCanonicalBase64Url(
    value.recordHash,
    RECORD_HASH_BYTES,
  );

  if (
    recordHash === undefined ||
    typeof value.recordHash !== 'string' ||
    value.recordHash.length !== RECORD_HASH_LENGTH
  ) {
    throw invalidInput();
  }

  return {
    purpose: value.purpose,
    value: Buffer.from(
      `${value.schemaVersion.toString()}\n${value.recordHash}\n${value.purpose}`,
      'utf8',
    ),
  };
}

export function validatePlaintext(
  value: unknown,
  purpose: IValidatedAad['purpose'],
): Buffer {
  if (!(value instanceof Uint8Array) || value.byteLength === 0) {
    throw invalidInput();
  }

  const plaintext = Buffer.from(value);

  if (purpose === 'refresh-token') {
    if (
      plaintext.byteLength > MAX_REFRESH_TOKEN_BYTES ||
      !isNonBlankUtf8(plaintext)
    ) {
      throw invalidInput();
    }

    return plaintext;
  }

  if (!PKCE_VERIFIER_PATTERN.test(plaintext.toString('ascii'))) {
    throw invalidInput();
  }

  return plaintext;
}

export function validateCiphertext(value: unknown): {
  readonly keyId: string;
  readonly nonce: Buffer;
  readonly tag: Buffer;
  readonly ciphertext: Buffer;
} {
  if (
    !isRecord(value) ||
    !hasOnlyKeys(value, ['keyId', 'nonce', 'tag', 'ciphertext'])
  ) {
    throw invalidInput();
  }

  if (!isValidKeyId(value.keyId)) {
    throw invalidInput();
  }

  const nonce = decodeCanonicalBase64Url(value.nonce, AES_GCM_NONCE_BYTES);
  const tag = decodeCanonicalBase64Url(value.tag, AES_GCM_TAG_BYTES);
  const ciphertext = decodeCanonicalBase64Url(value.ciphertext);

  if (
    nonce === undefined ||
    tag === undefined ||
    ciphertext === undefined ||
    ciphertext.byteLength === 0 ||
    ciphertext.byteLength > MAX_CIPHERTEXT_BYTES
  ) {
    throw invalidInput();
  }

  return { keyId: value.keyId, nonce, tag, ciphertext };
}

export function decodeCanonicalBase64Url(
  value: unknown,
  expectedByteLength?: number,
): Buffer | undefined {
  if (typeof value !== 'string' || !BASE64URL_PATTERN.test(value)) {
    return undefined;
  }

  const decoded = Buffer.from(value, 'base64url');

  if (
    decoded.byteLength === 0 ||
    decoded.toString('base64url') !== value ||
    (expectedByteLength !== undefined &&
      decoded.byteLength !== expectedByteLength)
  ) {
    return undefined;
  }

  return decoded;
}

export function isNonBlankUtf8(value: Buffer): boolean {
  try {
    return (
      new TextDecoder('utf-8', { fatal: true }).decode(value).trim().length > 0
    );
  } catch {
    return false;
  }
}

export function isValidKeyId(value: unknown): value is string {
  return (
    typeof value === 'string' &&
    KEY_ID_PATTERN.test(value) &&
    value.length <= MAX_KEY_ID_LENGTH
  );
}

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export function hasOnlyKeys(
  value: Record<string, unknown>,
  expectedKeys: readonly string[],
): boolean {
  const actualKeys = Object.keys(value);

  return (
    actualKeys.length === expectedKeys.length &&
    actualKeys.every((key) => expectedKeys.includes(key))
  );
}

export function invalidKeyRing(): PlatformAesKeyRingConfigurationError {
  return new PlatformAesKeyRingConfigurationError('INVALID_KEY_RING');
}

export function invalidInput(): PlatformSecretCipherError {
  return new PlatformSecretCipherError(
    'SECRET_CIPHER_INVALID_INPUT',
    INVALID_INPUT_MESSAGE,
  );
}

export function decryptionFailed(): PlatformSecretCipherError {
  return new PlatformSecretCipherError(
    'SECRET_CIPHER_DECRYPTION_FAILED',
    DECRYPTION_FAILED_MESSAGE,
  );
}

export function keyUnavailable(): PlatformSecretCipherError {
  return new PlatformSecretCipherError(
    'SECRET_CIPHER_KEY_UNAVAILABLE',
    KEY_UNAVAILABLE_MESSAGE,
  );
}
