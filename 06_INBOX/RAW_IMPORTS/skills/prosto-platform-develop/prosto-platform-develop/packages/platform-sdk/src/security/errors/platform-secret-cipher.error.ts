import { PlatformSdkError } from '@/errors/index.js';

/**
 * @alpha
 * Safe error codes emitted by secret-cipher implementations. They intentionally
 * contain no plaintext, ciphertext, key material, or provider diagnostics.
 */
export type PlatformSecretCipherErrorCodeType =
  | 'SECRET_CIPHER_INVALID_INPUT'
  | 'SECRET_CIPHER_DECRYPTION_FAILED'
  | 'SECRET_CIPHER_KEY_UNAVAILABLE';

/**
 * @alpha
 * Typed, secret-safe failure from an {@link IPlatformSecretCipher}.
 */
export class PlatformSecretCipherError extends PlatformSdkError {
  constructor(code: PlatformSecretCipherErrorCodeType, message: string) {
    super(code, message);
    this.name = 'PlatformSecretCipherError';
  }
}
