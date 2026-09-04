/**
 * @alpha
 * Safe configuration failure codes for the AES key-ring adapter.
 */
export type PlatformAesKeyRingConfigurationErrorCodeType = 'INVALID_KEY_RING';

/**
 * @alpha
 * Deterministic, redacted failure raised when an injected key ring is invalid.
 */
export class PlatformAesKeyRingConfigurationError extends Error {
  readonly code: PlatformAesKeyRingConfigurationErrorCodeType;

  constructor(code: PlatformAesKeyRingConfigurationErrorCodeType) {
    super('Invalid AES key-ring configuration.');
    this.name = 'PlatformAesKeyRingConfigurationError';
    this.code = code;
  }
}
