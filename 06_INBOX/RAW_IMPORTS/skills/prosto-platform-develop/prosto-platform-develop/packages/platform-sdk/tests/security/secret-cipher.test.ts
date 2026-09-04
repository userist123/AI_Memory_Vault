import {
  PlatformSecretCipherError,
  type PlatformSecretCipherErrorCodeType,
} from '@/index.js';
import { describe, expect, it } from 'vitest';

describe('PlatformSecretCipherError', (): void => {
  it('exposes a typed error without secret-bearing details', (): void => {
    // Arrange
    const code: PlatformSecretCipherErrorCodeType =
      'SECRET_CIPHER_DECRYPTION_FAILED';
    const message = 'Secret decryption failed.';

    // Act
    const error = new PlatformSecretCipherError(code, message);

    // Assert
    expect(error).toMatchObject({
      code,
      message,
      details: undefined,
    });
  });
});
