/**
 * @alpha
 * A fixed application purpose for encrypted browser-session secrets.
 * Implementations must bind this value, schema version, and record hash as
 * authenticated additional data; callers must not reuse ciphertext by purpose.
 */
export type PlatformSecretCipherPurposeType = 'refresh-token' | 'pkce-verifier';

/**
 * @alpha
 * Authenticated additional data for a persisted encrypted secret.
 * `recordHash` is a canonical, non-secret record identifier hash and
 * `schemaVersion` is a positive safe integer.
 */
export interface IPlatformSecretCipherAad {
  readonly schemaVersion: number;
  readonly recordHash: string;
  readonly purpose: PlatformSecretCipherPurposeType;
}

/**
 * @alpha
 * Persistable encrypted secret fields. Values are canonical base64url text
 * without padding; implementations validate their bounds before use.
 */
export interface IPlatformSecretCiphertext {
  readonly keyId: string;
  readonly nonce: string;
  readonly tag: string;
  readonly ciphertext: string;
}

/**
 * @alpha
 * Input for encrypting a secret with mandatory authenticated additional data.
 */
export interface IPlatformSecretCipherEncryptInput {
  readonly plaintext: Uint8Array;
  readonly aad: IPlatformSecretCipherAad;
}

/**
 * @alpha
 * Input for decrypting a persisted encrypted secret with its original AAD.
 */
export interface IPlatformSecretCipherDecryptInput {
  readonly ciphertext: IPlatformSecretCiphertext;
  readonly aad: IPlatformSecretCipherAad;
}

/**
 * @alpha
 * Successful secret decryption result. `requiresReencryption` signals that
 * the ciphertext used a non-active key and may be lazily rewrapped.
 */
export interface IPlatformSecretCipherDecryptResult {
  readonly plaintext: Uint8Array;
  readonly requiresReencryption: boolean;
}

/**
 * @alpha
 * Framework- and key-provider-neutral port for authenticated secret storage.
 * Implementations must never include plaintext or key material in errors.
 */
export interface IPlatformSecretCipher {
  encrypt(
    input: IPlatformSecretCipherEncryptInput,
  ): Promise<IPlatformSecretCiphertext>;
  decrypt(
    input: IPlatformSecretCipherDecryptInput,
  ): Promise<IPlatformSecretCipherDecryptResult>;
}
