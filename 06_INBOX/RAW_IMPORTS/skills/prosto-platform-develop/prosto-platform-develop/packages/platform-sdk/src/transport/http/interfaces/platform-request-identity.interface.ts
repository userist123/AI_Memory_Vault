/**
 * @alpha
 * Anonymous identity — no authenticated subject.
 */
export interface IPlatformAnonymousIdentity {
  readonly authenticationType: 'anonymous';
  readonly roles: readonly string[];
  readonly permissions: readonly string[];
}

/**
 * @alpha
 * Delegated identity — authenticated subject with roles and permissions.
 */
export interface IPlatformDelegatedIdentity {
  readonly authenticationType: 'delegated';
  readonly subjectId: string;
  readonly roles: readonly string[];
  readonly permissions: readonly string[];
}

/**
 * @alpha
 * Discriminated union of possible request identity variants.
 */
export type PlatformRequestIdentityType =
  | IPlatformAnonymousIdentity
  | IPlatformDelegatedIdentity;
