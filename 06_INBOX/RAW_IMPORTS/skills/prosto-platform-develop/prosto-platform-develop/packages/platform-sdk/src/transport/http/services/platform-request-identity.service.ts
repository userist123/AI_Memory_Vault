import type {
  IPlatformAnonymousIdentity,
  IPlatformDelegatedIdentity,
} from '../interfaces/index.js';
import { freezeStringArray } from '@/utils/index.js';
import { PlatformHttpError } from '../errors/index.js';

export interface IPlatformAnonymousIdentityInput {
  readonly roles?: readonly string[];
  readonly permissions?: readonly string[];
}

/**
 * @alpha
 * Immutable anonymous identity value object.
 */
export class PlatformAnonymousIdentity implements IPlatformAnonymousIdentity {
  readonly authenticationType = 'anonymous' as const;
  readonly roles: readonly string[];
  readonly permissions: readonly string[];

  constructor(input?: IPlatformAnonymousIdentityInput) {
    this.roles = freezeStringArray(input?.roles ?? []);
    this.permissions = freezeStringArray(input?.permissions ?? []);

    Object.freeze(this);
  }
}

export interface IPlatformDelegatedIdentityInput {
  readonly subjectId: string;
  readonly roles?: readonly string[];
  readonly permissions?: readonly string[];
}

/**
 * @alpha
 * Immutable delegated identity value object.
 * Rejects empty `subjectId`.
 */
export class PlatformDelegatedIdentity implements IPlatformDelegatedIdentity {
  readonly authenticationType = 'delegated' as const;
  readonly subjectId: string;
  readonly roles: readonly string[];
  readonly permissions: readonly string[];

  constructor(input: IPlatformDelegatedIdentityInput) {
    if (!input.subjectId.trim().length) {
      throw new PlatformHttpError(
        'HTTP_UNAUTHENTICATED',
        'Delegated identity requires a non-empty subjectId.',
        { subjectId: input.subjectId },
      );
    }

    this.subjectId = input.subjectId;
    this.roles = freezeStringArray(input.roles ?? []);
    this.permissions = freezeStringArray(input.permissions ?? []);

    Object.freeze(this);
  }
}
