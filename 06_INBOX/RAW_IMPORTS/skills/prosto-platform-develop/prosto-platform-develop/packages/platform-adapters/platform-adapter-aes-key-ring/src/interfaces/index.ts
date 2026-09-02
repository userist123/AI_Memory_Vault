import type { KeyObject } from 'node:crypto';

export interface IValidatedKeyRing {
  readonly activeKeyId: string;
  readonly keys: ReadonlyMap<string, KeyObject>;
}

export interface IValidatedAad {
  readonly purpose: 'refresh-token' | 'pkce-verifier';
  readonly value: Buffer;
}
