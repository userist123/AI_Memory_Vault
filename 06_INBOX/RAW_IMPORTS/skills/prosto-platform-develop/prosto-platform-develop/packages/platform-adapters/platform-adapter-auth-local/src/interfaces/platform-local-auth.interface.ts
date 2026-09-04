import type {
  IPlatformHttpRouteRegistration,
  IPlatformRequestIdentityResolver,
} from '@prosto/platform-sdk';

/** @alpha */
export interface IPlatformLocalAuthAccount {
  readonly id: string;
  readonly username: string;
  readonly passwordHash: string;
  readonly roles: readonly string[];
  readonly permissions: readonly string[];
  readonly mustChangePassword: boolean;
  readonly disabledAt?: number;
  readonly lockoutUntil?: number;
}

/** @alpha */
export interface IPlatformLocalAuthSession {
  readonly sessionTokenHash: string;
  readonly accountId: string;
  readonly csrfTokenHash: string;
  readonly createdAt: number;
  readonly lastSeenAt: number;
  readonly idleExpiresAt: number;
  readonly absoluteExpiresAt: number;
}

/** @alpha */
export interface IPlatformLocalAuthSessionStore {
  findAccountByUsername(
    normalizedUsername: string,
  ): Promise<IPlatformLocalAuthAccount | undefined>;
  findAccountById(
    accountId: string,
  ): Promise<IPlatformLocalAuthAccount | undefined>;
  updateAccountPassword(input: {
    readonly accountId: string;
    readonly passwordHash: string;
    readonly mustChangePassword: boolean;
  }): Promise<boolean>;
  findSession(
    sessionTokenHash: string,
  ): Promise<IPlatformLocalAuthSession | undefined>;
  touchSession(input: {
    readonly sessionTokenHash: string;
    readonly lastSeenAt: number;
    readonly idleExpiresAt: number;
  }): Promise<void>;
  rotateSessionCsrfToken(input: {
    readonly sessionTokenHash: string;
    readonly csrfTokenHash: string;
  }): Promise<boolean>;
  deleteSession(sessionTokenHash: string): Promise<void>;
  replaceAccountSessions(input: {
    readonly accountId: string;
    readonly session: IPlatformLocalAuthSession;
  }): Promise<void>;
}

/** @alpha */
export interface IPlatformLocalAuthClock {
  now(): number;
}

/** @alpha */
export interface IPlatformLocalAuthRandomness {
  base64Url(bytes: number): string;
}

/** @alpha */
export interface IPlatformLocalAuthPasswordHasher {
  hash(password: string): Promise<string>;
  verify(passwordHash: string, password: string): Promise<boolean>;
  verifyUnknown(password: string): Promise<void>;
  needsRehash(passwordHash: string): boolean;
}

/** @alpha */
export interface IPlatformLocalAuthFailedLoginLimiter {
  isBlocked(normalizedUsername: string, now: number): Promise<boolean>;
  recordFailure(normalizedUsername: string, now: number): Promise<void>;
  clearFailures(normalizedUsername: string): Promise<void>;
}

/** @alpha */
export type PlatformLocalAuthLogEventType =
  | 'login_succeeded'
  | 'login_rejected'
  | 'password_changed'
  | 'session_expired'
  | 'logout';

/** @alpha */
export interface IPlatformLocalAuthLogEvent {
  readonly event: PlatformLocalAuthLogEventType;
  readonly correlationId: string;
  readonly outcome: 'succeeded' | 'rejected' | 'unavailable';
  readonly durationMs: number;
}

/** @alpha */
export interface IPlatformLocalAuthLogger {
  log(event: IPlatformLocalAuthLogEvent): void;
}

/** @alpha */
export interface IPlatformLocalAuthRuntimeConfig {
  readonly origin: string;
  readonly sessionIdleTtlMs?: number;
  readonly sessionAbsoluteTtlMs?: number;
  readonly minimumPasswordLength?: number;
  readonly secureCookies?: boolean;
}

/** @alpha */
export interface IPlatformLocalAuthRuntimeDependencies {
  readonly store: IPlatformLocalAuthSessionStore;
  readonly passwordHasher: IPlatformLocalAuthPasswordHasher;
  readonly limiter: IPlatformLocalAuthFailedLoginLimiter;
  readonly randomness: IPlatformLocalAuthRandomness;
  readonly clock: IPlatformLocalAuthClock;
  readonly logger?: IPlatformLocalAuthLogger;
}

/** @alpha */
export interface IPlatformLocalAuthRuntime {
  readonly resolver: IPlatformRequestIdentityResolver;
  readonly routes: readonly IPlatformHttpRouteRegistration[];
}

/** @alpha */
export interface IResolvedPlatformLocalAuthRuntimeConfig extends Required<
  Pick<
    IPlatformLocalAuthRuntimeConfig,
    | 'sessionIdleTtlMs'
    | 'sessionAbsoluteTtlMs'
    | 'minimumPasswordLength'
    | 'secureCookies'
  >
> {
  readonly origin: string;
}
