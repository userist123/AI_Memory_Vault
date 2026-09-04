import type { IPlatformIdentityResolutionRequest } from '../interfaces/index.js';
import type { PlatformHttpMethodType } from '../types/index.js';
import { freezeRecord, freezeRecordOfArrays } from '@/utils/index.js';

export interface IPlatformIdentityResolutionRequestInput {
  readonly correlationId: string;
  readonly method: PlatformHttpMethodType;
  readonly path: string;
  readonly headers?: Readonly<Record<string, readonly string[]>>;
  readonly params?: Readonly<Record<string, string>>;
  readonly query?: Readonly<Record<string, readonly string[]>>;
}

/**
 * @alpha
 * Immutable narrow input-port for identity resolution.
 * Defensive-copies metadata and does not accept/store body.
 */
export class PlatformIdentityResolutionRequest implements IPlatformIdentityResolutionRequest {
  readonly correlationId: string;
  readonly method: PlatformHttpMethodType;
  readonly path: string;
  readonly headers: Readonly<Record<string, readonly string[]>>;
  readonly params: Readonly<Record<string, string>>;
  readonly query: Readonly<Record<string, readonly string[]>>;

  constructor(input: IPlatformIdentityResolutionRequestInput) {
    this.correlationId = input.correlationId;
    this.method = input.method;
    this.path = input.path;
    this.headers = freezeRecordOfArrays(input.headers ?? {});
    this.params = freezeRecord(input.params ?? {});
    this.query = freezeRecordOfArrays(input.query ?? {});

    Object.freeze(this);
  }
}
