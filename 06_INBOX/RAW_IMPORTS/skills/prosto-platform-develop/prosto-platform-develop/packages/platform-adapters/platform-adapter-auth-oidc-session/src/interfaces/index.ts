import type {
  IPlatformHttpRouteRegistration,
  IPlatformRequestIdentityResolver,
  IPlatformSecretCipher,
  IPlatformSecretCiphertext,
} from '@prosto/platform-sdk';

/** @alpha */
export type PlatformOidcSessionAlgorithmType = 'RS256' | 'PS256' | 'ES256';

/** @alpha */
export interface IPlatformOidcSessionClock {
  now(): number;
  sleep(milliseconds: number): Promise<void>;
}

/** @alpha */
export interface IPlatformOidcSessionRecord {
  readonly sessionIdHash: string;
  readonly subjectId: string;
  readonly roles: readonly string[];
  readonly permissions: readonly string[];
  readonly createdAt: number;
  readonly lastSeenAt: number;
  readonly absoluteExpiresAt: number;
  readonly accessExpiresAt: number;
  readonly refreshToken: IPlatformSecretCiphertext;
  readonly refreshLeaseId?: string;
  readonly refreshLeaseExpiresAt?: number;
}

/** @alpha */
export interface IPlatformOidcTransactionRecord {
  readonly transactionIdHash: string;
  readonly stateHash: string;
  readonly nonce: string;
  readonly expiresAt: number;
  readonly pkceVerifier: IPlatformSecretCiphertext;
  readonly replacedSessionIdHash?: string;
}

/** @alpha */
export interface IPlatformOidcSessionStore {
  findSession(
    sessionIdHash: string,
  ): Promise<IPlatformOidcSessionRecord | undefined>;
  touchSession(sessionIdHash: string, now: number): Promise<void>;
  deleteSession(sessionIdHash: string): Promise<void>;
  createTransaction(record: IPlatformOidcTransactionRecord): Promise<void>;
  findTransaction(
    transactionIdHash: string,
    stateHash: string,
    now: number,
  ): Promise<IPlatformOidcTransactionRecord | undefined>;
  consumeTransaction(
    transactionIdHash: string,
    stateHash: string,
  ): Promise<void>;
  createSessionFromTransaction(input: {
    readonly transactionIdHash: string;
    readonly stateHash: string;
    readonly session: IPlatformOidcSessionRecord;
    readonly replacedSessionIdHash?: string;
  }): Promise<boolean>;
  acquireRefreshLease(input: {
    readonly sessionIdHash: string;
    readonly leaseId: string;
    readonly leaseExpiresAt: number;
    readonly now: number;
  }): Promise<'acquired' | 'held' | 'missing'>;
  releaseRefreshLease(sessionIdHash: string, leaseId: string): Promise<void>;
  updateSessionAfterRefresh(input: {
    readonly sessionIdHash: string;
    readonly leaseId: string;
    readonly subjectId: string;
    readonly roles: readonly string[];
    readonly permissions: readonly string[];
    readonly accessExpiresAt: number;
    readonly refreshToken: IPlatformSecretCiphertext;
  }): Promise<boolean>;
}

/** @alpha */
export interface IPlatformOidcTokenSet {
  readonly accessToken: string;
  readonly refreshToken: string;
  readonly expiresIn: number;
}

/** @alpha */
export interface IPlatformOidcClientFacade {
  createAuthorizationUrl(input: {
    readonly state: string;
    readonly nonce: string;
    readonly pkceVerifier: string;
  }): Promise<string>;
  exchangeAuthorizationCode(input: {
    readonly code: string;
    readonly state: string;
    readonly nonce: string;
    readonly pkceVerifier: string;
  }): Promise<IPlatformOidcTokenSet>;
  refresh(refreshToken: string): Promise<IPlatformOidcTokenSet>;
  revoke(refreshToken: string): Promise<void>;
  isInvalidGrant(error: unknown): boolean;
}

/** @alpha */
export type PlatformOidcSessionLogEventType =
  | 'login_started'
  | 'login_succeeded'
  | 'login_failed'
  | 'callback_rejected'
  | 'refresh_succeeded'
  | 'refresh_unavailable'
  | 'refresh_revoked'
  | 'logout'
  | 'revocation_unavailable'
  | 'session_expired';

/** @alpha */
export interface IPlatformOidcSessionLogEvent {
  readonly event: PlatformOidcSessionLogEventType;
  readonly correlationId: string;
  readonly outcome: 'succeeded' | 'failed' | 'unavailable' | 'rejected';
  readonly durationMs: number;
}

/** @alpha */
export interface IPlatformOidcSessionLogger {
  log(event: IPlatformOidcSessionLogEvent): void;
}

/** @alpha */
export interface IPlatformOidcSessionRuntimeConfig {
  readonly issuer: string;
  readonly jwksUri: string;
  readonly authorizationEndpoint: string;
  readonly tokenEndpoint: string;
  readonly revocationEndpoint: string;
  readonly redirectUri: string;
  readonly clientId: string;
  readonly clientSecret: string;
  readonly scopes: readonly string[];
  readonly audiences: readonly string[];
  readonly resource?: string;
  readonly allowedAlgorithms?: readonly PlatformOidcSessionAlgorithmType[];
  readonly cookieVersion?: number;
}

/** @alpha */
export interface IPlatformOidcSessionRuntimeDependencies {
  readonly store: IPlatformOidcSessionStore;
  readonly cipher: IPlatformSecretCipher;
  readonly accessTokenResolver: IPlatformRequestIdentityResolver;
  readonly oidcClient?: IPlatformOidcClientFacade;
  readonly clock?: IPlatformOidcSessionClock;
  readonly logger?: IPlatformOidcSessionLogger;
}

/** @alpha */
export interface IPlatformOidcSessionRuntime {
  readonly resolver: IPlatformRequestIdentityResolver;
  readonly routes: readonly IPlatformHttpRouteRegistration[];
}

/** @alpha */
export interface IResolvedConfig extends IPlatformOidcSessionRuntimeConfig {
  readonly allowedAlgorithms: readonly ('RS256' | 'PS256' | 'ES256')[];
  readonly cookieVersion: number;
  readonly sessionCookieName: string;
}
