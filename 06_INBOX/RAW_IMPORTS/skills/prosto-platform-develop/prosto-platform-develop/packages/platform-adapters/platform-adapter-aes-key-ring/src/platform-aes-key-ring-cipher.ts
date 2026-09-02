import type { IValidatedKeyRing } from '@/interfaces/index.js';
import {
  createCipheriv,
  createDecipheriv,
  type KeyObject,
  randomBytes,
} from 'node:crypto';
import {
  type IPlatformSecretCipher,
  type IPlatformSecretCipherDecryptInput,
  type IPlatformSecretCipherDecryptResult,
  type IPlatformSecretCipherEncryptInput,
  type IPlatformSecretCiphertext,
  PlatformSecretCipherError,
} from '@prosto/platform-sdk';
import {
  decryptionFailed,
  invalidInput,
  keyUnavailable,
  validateAad,
  validateCiphertext,
  validateKeyRing,
  validatePlaintext,
} from '@/utils/index.js';
import { AES_GCM_NONCE_BYTES, AES_GCM_TAG_BYTES } from '@/constants/index.js';

/**
 * @alpha
 * Creates a secret cipher from a deployment-injected, versioned AES key ring.
 * The key ring must be an object with `activeKeyId` and one to three `{ id, key
 * }` records, where keys are canonical base64url-encoded 32-byte values. Raw
 * keys are converted to private Node key objects and are never returned.
 */
export function createPlatformAesKeyRingCipher(
  keyRing: unknown,
): IPlatformSecretCipher {
  const validatedKeyRing = validateKeyRing(keyRing);

  return new PlatformAesKeyRingCipher(validatedKeyRing);
}

class PlatformAesKeyRingCipher implements IPlatformSecretCipher {
  private readonly _activeKeyId: string;
  private readonly _keys: ReadonlyMap<string, KeyObject>;

  constructor(keyRing: IValidatedKeyRing) {
    this._activeKeyId = keyRing.activeKeyId;
    this._keys = keyRing.keys;
  }

  async encrypt(
    input: IPlatformSecretCipherEncryptInput,
  ): Promise<IPlatformSecretCiphertext> {
    const aad = validateAad(input?.aad);
    const plaintext = validatePlaintext(input?.plaintext, aad.purpose);
    const key = this._keys.get(this._activeKeyId);

    if (key === undefined) {
      throw keyUnavailable();
    }

    try {
      const nonce = randomBytes(AES_GCM_NONCE_BYTES);
      const cipher = createCipheriv('aes-256-gcm', key, nonce, {
        authTagLength: AES_GCM_TAG_BYTES,
      });

      cipher.setAAD(aad.value);

      const ciphertext = Buffer.concat([
        cipher.update(plaintext),
        cipher.final(),
      ]);

      return Object.freeze({
        keyId: this._activeKeyId,
        nonce: nonce.toString('base64url'),
        tag: cipher.getAuthTag().toString('base64url'),
        ciphertext: ciphertext.toString('base64url'),
      });
    } catch (error: unknown) {
      if (error instanceof PlatformSecretCipherError) {
        throw error;
      }

      throw invalidInput();
    }
  }

  async decrypt(
    input: IPlatformSecretCipherDecryptInput,
  ): Promise<IPlatformSecretCipherDecryptResult> {
    const aad = validateAad(input?.aad);
    const fields = validateCiphertext(input?.ciphertext);
    const key = this._keys.get(fields.keyId);

    if (key === undefined) {
      throw keyUnavailable();
    }

    try {
      const decipher = createDecipheriv('aes-256-gcm', key, fields.nonce, {
        authTagLength: AES_GCM_TAG_BYTES,
      });
      decipher.setAAD(aad.value);
      decipher.setAuthTag(fields.tag);
      const plaintext = Buffer.concat([
        decipher.update(fields.ciphertext),
        decipher.final(),
      ]);
      const validatedPlaintext = validatePlaintext(plaintext, aad.purpose);

      return Object.freeze({
        plaintext: new Uint8Array(validatedPlaintext),
        requiresReencryption: fields.keyId !== this._activeKeyId,
      });
    } catch (error: unknown) {
      if (error instanceof PlatformSecretCipherError) {
        throw error;
      }

      throw decryptionFailed();
    }
  }
}
