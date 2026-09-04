import { createHash } from 'node:crypto';
import { describe, expect, it } from 'vitest';
import {
  createPlatformAesKeyRingCipher,
  PlatformAesKeyRingConfigurationError,
} from '@/index.js';
import {
  type IPlatformSecretCipher,
  type IPlatformSecretCipherAad,
  type IPlatformSecretCiphertext,
  PlatformSecretCipherError,
} from '@prosto/platform-sdk';

const ACTIVE_KEY = Buffer.alloc(32, 17).toString('base64url');
const RETIRED_KEY = Buffer.alloc(32, 34).toString('base64url');
const AAD: IPlatformSecretCipherAad = {
  schemaVersion: 1,
  recordHash: createHash('sha256').update('session-record').digest('base64url'),
  purpose: 'refresh-token',
};
const PLAINTEXT = new TextEncoder().encode('rotated-refresh-token-value');

function createKeyRing(
  activeKeyId = 'active-2026',
  keys: readonly { readonly id: string; readonly key: string }[] = [
    { id: 'active-2026', key: ACTIVE_KEY },
  ],
): unknown {
  return { activeKeyId, keys };
}

function alterBase64Url(value: string): string {
  const bytes = Buffer.from(value, 'base64url');

  if (bytes.byteLength === 0) {
    throw new Error('Expected a non-empty base64url value.');
  }

  const lastIndex = bytes.byteLength - 1;
  bytes[lastIndex] = (bytes[lastIndex] ?? 0) ^ 1;

  return bytes.toString('base64url');
}

async function captureError(action: () => Promise<unknown>): Promise<unknown> {
  try {
    await action();
  } catch (error: unknown) {
    return error;
  }

  throw new Error('Expected action to throw.');
}

describe('createPlatformAesKeyRingCipher', (): void => {
  it('encrypts with the active key and decrypts only with matching AAD', async (): Promise<void> => {
    // Arrange
    const cipher = createPlatformAesKeyRingCipher(createKeyRing());

    // Act
    const encrypted = await cipher.encrypt({ plaintext: PLAINTEXT, aad: AAD });
    const decrypted = await cipher.decrypt({ ciphertext: encrypted, aad: AAD });

    // Assert
    expect(encrypted).toMatchObject({ keyId: 'active-2026' });
    expect(encrypted.ciphertext).not.toContain('rotated-refresh-token-value');
    expect(Buffer.from(decrypted.plaintext).toString('utf8')).toBe(
      'rotated-refresh-token-value',
    );
    expect(decrypted.requiresReencryption).toBe(false);
    await expect(
      cipher.decrypt({
        ciphertext: encrypted,
        aad: { ...AAD, purpose: 'pkce-verifier' },
      }),
    ).rejects.toMatchObject({
      code: 'SECRET_CIPHER_DECRYPTION_FAILED',
    });
  });

  it('rejects malformed or unsafe key-ring configuration with one redacted error', (): void => {
    // Arrange
    const invalidRings: readonly unknown[] = [
      undefined,
      null,
      'not-a-key-ring',
      { activeKeyId: 'active-2026', keys: [] },
      createKeyRing('missing-key'),
      createKeyRing('active-2026', [
        { id: 'active-2026', key: ACTIVE_KEY },
        { id: 'active-2026', key: RETIRED_KEY },
      ]),
      createKeyRing('active-2026', [
        { id: 'active-2026', key: 'not-base64url=' },
      ]),
      createKeyRing('active-2026', [
        { id: 'active-2026', key: Buffer.alloc(31, 17).toString('base64url') },
      ]),
      { activeKeyId: 'active-2026', keys: [], unexpected: true },
    ];

    // Act and Assert
    for (const keyRing of invalidRings) {
      expect(() => createPlatformAesKeyRingCipher(keyRing)).toThrow(
        PlatformAesKeyRingConfigurationError,
      );

      try {
        createPlatformAesKeyRingCipher(keyRing);
      } catch (error: unknown) {
        expect(error).toMatchObject({
          code: 'INVALID_KEY_RING',
          message: 'Invalid AES key-ring configuration.',
        });
        expect((error as Error).message).not.toContain(ACTIVE_KEY);
      }
    }
  });

  it('rejects corrupted authentication tags and ciphertexts without diagnostics', async (): Promise<void> => {
    // Arrange
    const cipher = createPlatformAesKeyRingCipher(createKeyRing());
    const encrypted = await cipher.encrypt({ plaintext: PLAINTEXT, aad: AAD });
    const corruptions: readonly IPlatformSecretCiphertext[] = [
      { ...encrypted, tag: alterBase64Url(encrypted.tag) },
      { ...encrypted, ciphertext: alterBase64Url(encrypted.ciphertext) },
    ];

    // Act and Assert
    for (const ciphertext of corruptions) {
      const error = await captureError(() =>
        cipher.decrypt({ ciphertext, aad: AAD }),
      );

      expect(error).toBeInstanceOf(PlatformSecretCipherError);
      expect(error).toMatchObject({
        code: 'SECRET_CIPHER_DECRYPTION_FAILED',
        message: 'Secret cipher decryption failed.',
      });
      expect((error as Error).message).not.toContain(encrypted.ciphertext);
    }
  });

  it('signals lazy re-encryption after decrypting with a retained key', async (): Promise<void> => {
    // Arrange
    const oldCipher = createPlatformAesKeyRingCipher(
      createKeyRing('old-2025', [{ id: 'old-2025', key: RETIRED_KEY }]),
    );
    const encryptedWithOldKey = await oldCipher.encrypt({
      plaintext: PLAINTEXT,
      aad: AAD,
    });
    const rotatedCipher = createPlatformAesKeyRingCipher(
      createKeyRing('active-2026', [
        { id: 'active-2026', key: ACTIVE_KEY },
        { id: 'old-2025', key: RETIRED_KEY },
      ]),
    );

    // Act
    const decrypted = await rotatedCipher.decrypt({
      ciphertext: encryptedWithOldKey,
      aad: AAD,
    });
    const reencrypted = await rotatedCipher.encrypt({
      plaintext: decrypted.plaintext,
      aad: AAD,
    });

    // Assert
    expect(decrypted.requiresReencryption).toBe(true);
    expect(reencrypted.keyId).toBe('active-2026');
    await expect(
      rotatedCipher.decrypt({ ciphertext: reencrypted, aad: AAD }),
    ).resolves.toMatchObject({ requiresReencryption: false });
  });

  it('rejects invalid input and unavailable keys with stable secret-safe errors', async (): Promise<void> => {
    // Arrange
    const cipher = createPlatformAesKeyRingCipher(createKeyRing());
    const encrypted = await cipher.encrypt({ plaintext: PLAINTEXT, aad: AAD });
    const unavailableCiphertext = { ...encrypted, keyId: 'removed-key' };

    // Act
    const invalidInputError = await captureError(() =>
      cipher.encrypt({
        plaintext: new Uint8Array(),
        aad: AAD,
      }),
    );
    const unavailableKeyError = await captureError(() =>
      cipher.decrypt({ ciphertext: unavailableCiphertext, aad: AAD }),
    );

    // Assert
    expect(invalidInputError).toMatchObject({
      code: 'SECRET_CIPHER_INVALID_INPUT',
      message: 'Secret cipher input is invalid.',
    });
    expect(unavailableKeyError).toMatchObject({
      code: 'SECRET_CIPHER_KEY_UNAVAILABLE',
      message: 'Secret cipher key is unavailable.',
    });
  });

  it('enforces purpose-specific plaintext and canonical persisted fields', async (): Promise<void> => {
    // Arrange
    const cipher: IPlatformSecretCipher =
      createPlatformAesKeyRingCipher(createKeyRing());

    // Act and Assert
    await expect(
      cipher.encrypt({
        plaintext: new TextEncoder().encode('too-short'),
        aad: { ...AAD, purpose: 'pkce-verifier' },
      }),
    ).rejects.toMatchObject({ code: 'SECRET_CIPHER_INVALID_INPUT' });
    await expect(
      cipher.decrypt({
        ciphertext: {
          keyId: 'active-2026',
          nonce: 'invalid=',
          tag: 'invalid',
          ciphertext: 'invalid',
        },
        aad: AAD,
      }),
    ).rejects.toMatchObject({ code: 'SECRET_CIPHER_INVALID_INPUT' });
  });
});
