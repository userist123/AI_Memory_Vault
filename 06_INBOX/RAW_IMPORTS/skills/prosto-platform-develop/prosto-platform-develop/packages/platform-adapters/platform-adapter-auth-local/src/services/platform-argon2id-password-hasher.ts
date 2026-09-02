import argon2 from 'argon2';
import type { IPlatformLocalAuthPasswordHasher } from '@/interfaces/index.js';

/**
 * @alpha
 * Argon2id policy used by the local authentication adapter. Changes require a
 * rehash through {@link PlatformArgon2idPasswordHasher.needsRehash}.
 */
export const PLATFORM_LOCAL_AUTH_ARGON2ID_OPTIONS = Object.freeze({
  type: argon2.argon2id,
  memoryCost: 19 * 1024,
  timeCost: 2,
  parallelism: 1,
  hashLength: 32,
});

/** @alpha */
export class PlatformArgon2idPasswordHasher implements IPlatformLocalAuthPasswordHasher {
  async hash(password: string): Promise<string> {
    return argon2.hash(password, PLATFORM_LOCAL_AUTH_ARGON2ID_OPTIONS);
  }

  async verify(passwordHash: string, password: string): Promise<boolean> {
    try {
      return await argon2.verify(passwordHash, password);
    } catch {
      return false;
    }
  }

  async verifyUnknown(password: string): Promise<void> {
    await this.hash(password);
  }

  needsRehash(passwordHash: string): boolean {
    try {
      return argon2.needsRehash(
        passwordHash,
        PLATFORM_LOCAL_AUTH_ARGON2ID_OPTIONS,
      );
    } catch {
      return true;
    }
  }
}
