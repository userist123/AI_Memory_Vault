import { randomBytes } from 'node:crypto';
import type {
  IPlatformLocalAuthClock,
  IPlatformLocalAuthRandomness,
} from '@prosto/platform-adapter-auth-local';

/** @internal */
export class SystemLocalAuthClock implements IPlatformLocalAuthClock {
  now(): number {
    return Date.now();
  }
}

/** @internal */
export class CryptoLocalAuthRandomness implements IPlatformLocalAuthRandomness {
  base64Url(bytes: number): string {
    return randomBytes(bytes).toString('base64url');
  }
}
