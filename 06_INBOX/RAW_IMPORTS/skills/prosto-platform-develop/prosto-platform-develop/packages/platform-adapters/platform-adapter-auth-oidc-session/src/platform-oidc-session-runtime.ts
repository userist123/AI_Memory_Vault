import type {
  IPlatformHttpRequest,
  IPlatformHttpResponse,
  IPlatformHttpRouteContextFactoryInput,
  IPlatformHttpRouteRegistration,
  IPlatformIdentityResolutionRequest,
  IPlatformRequestIdentityResolver,
  IPlatformSecretCipher,
  PlatformRequestIdentityType,
} from '@prosto/platform-sdk';
import {
  PlatformAnonymousIdentity,
  PlatformDelegatedIdentity,
  PlatformHttpError,
  PlatformHttpResponse,
} from '@prosto/platform-sdk';
import type {
  IPlatformOidcClientFacade,
  IPlatformOidcSessionClock,
  IPlatformOidcSessionLogEvent,
  IPlatformOidcSessionLogger,
  IPlatformOidcSessionRecord,
  IPlatformOidcSessionRuntime,
  IPlatformOidcSessionRuntimeConfig,
  IPlatformOidcSessionRuntimeDependencies,
  IPlatformOidcSessionStore,
  IResolvedConfig,
} from '@/interfaces/index.js';
import { createOpenIdClientFacade } from './openid-client-facade.js';
import {
  ACCESS_REFRESH_WINDOW_MS,
  OIDC_TRANSACTION_TTL_SECONDS,
  REFRESH_LEASE_MS,
  REFRESH_POLL_MS,
  REFRESH_WAIT_MS,
  SESSION_ABSOLUTE_TTL_MS,
  SESSION_SCHEMA_VERSION,
  SESSION_TOUCH_INTERVAL_MS,
  TRANSACTION_COOKIE_NAME,
} from '@/constants/index.js';
import {
  clearSessionCookie,
  clearTransactionCookie,
  equalSecrets,
  failedCallbackResponse,
  hasExpired,
  hashOpaqueValue,
  identityFromSession,
  isOpaqueId,
  isPkceVerifier,
  isSessionRecord,
  isTransactionRecord,
  parseCallback,
  parseSingleCookie,
  randomBase64Url,
  redirect,
  resolveConfig,
  sessionCookie,
  systemClock,
  transactionCookie,
  unauthenticated,
  unavailable,
  validateTokenSet,
} from '@/utils/index.js';

/**
 * @alpha
 * Creates a framework-neutral OIDC broker runtime with the session resolver and
 * route registrations required by an SDK HTTP server.
 */
export function createPlatformOidcSessionRuntime(
  config: IPlatformOidcSessionRuntimeConfig,
  dependencies: IPlatformOidcSessionRuntimeDependencies,
): IPlatformOidcSessionRuntime {
  const resolvedConfig = resolveConfig(config);
  const oidcClient =
    dependencies.oidcClient ?? createOpenIdClientFacade(resolvedConfig);
  const runtime = new PlatformOidcSessionRuntime(
    resolvedConfig,
    dependencies.store,
    dependencies.cipher,
    dependencies.accessTokenResolver,
    oidcClient,
    dependencies.clock ?? systemClock,
    dependencies.logger,
  );

  return Object.freeze({
    resolver: runtime.resolver,
    routes: runtime.routes,
  });
}

/** @alpha */
class PlatformOidcSessionRuntime {
  readonly resolver: IPlatformRequestIdentityResolver;
  readonly routes: readonly IPlatformHttpRouteRegistration[];

  constructor(
    private readonly _config: IResolvedConfig,
    private readonly _store: IPlatformOidcSessionStore,
    private readonly _cipher: IPlatformSecretCipher,
    private readonly _accessTokenResolver: IPlatformRequestIdentityResolver,
    private readonly _oidcClient: IPlatformOidcClientFacade,
    private readonly _clock: IPlatformOidcSessionClock,
    private readonly _logger: IPlatformOidcSessionLogger | undefined,
  ) {
    this.resolver = new PlatformOidcSessionResolver(this);
    this.routes = Object.freeze([
      new SessionRouteRegistration('GET', '/auth/login', (request) =>
        this._login(request),
      ),
      new SessionRouteRegistration('GET', '/auth/callback', (request) =>
        this._callback(request),
      ),
      new SessionRouteRegistration('POST', '/auth/logout', (request) =>
        this._logout(request),
      ),
    ]);
  }

  async resolveIdentity(
    request: IPlatformIdentityResolutionRequest,
  ): Promise<PlatformRequestIdentityType> {
    const startedAt = this._clock.now();
    let sessionId: string | undefined;

    try {
      sessionId = parseSingleCookie(
        request.headers.cookie,
        this._config.sessionCookieName,
      );
    } catch {
      throw unauthenticated();
    }

    try {
      if (sessionId === undefined) {
        return new PlatformAnonymousIdentity();
      }
      if (!isOpaqueId(sessionId)) {
        throw unauthenticated();
      }

      const sessionIdHash = hashOpaqueValue(sessionId);
      const session = await this._store.findSession(sessionIdHash);
      const now = this._clock.now();

      if (session === undefined) {
        return new PlatformAnonymousIdentity();
      }

      if (!isSessionRecord(session) || hasExpired(session, now)) {
        void this._deleteSessionQuietly(sessionIdHash);
        this._log(
          'session_expired',
          request.correlationId,
          'succeeded',
          startedAt,
        );

        return new PlatformAnonymousIdentity();
      }

      const identity = identityFromSession(session);

      if (session.lastSeenAt + SESSION_TOUCH_INTERVAL_MS <= now) {
        await this._store.touchSession(sessionIdHash, now);
      }

      if (session.accessExpiresAt - now > ACCESS_REFRESH_WINDOW_MS) {
        return identity;
      }

      return this._refreshOrReturn(session, identity, request.correlationId);
    } catch (error: unknown) {
      if (error instanceof PlatformHttpError) {
        throw error;
      }

      throw unavailable();
    } finally {
      // Cookie values and session identifiers are intentionally never logged.
      void sessionId;
    }
  }

  private async _refreshOrReturn(
    session: IPlatformOidcSessionRecord,
    identity: PlatformRequestIdentityType,
    correlationId: string,
  ): Promise<PlatformRequestIdentityType> {
    const now = this._clock.now();
    const leaseId = randomBase64Url(32);
    const lease = await this._store.acquireRefreshLease({
      sessionIdHash: session.sessionIdHash,
      leaseId,
      leaseExpiresAt: now + REFRESH_LEASE_MS,
      now,
    });

    if (lease === 'missing') {
      return new PlatformAnonymousIdentity();
    }

    if (lease === 'held') {
      if (session.accessExpiresAt > now) {
        return identity;
      }

      return this._waitForRefresh(session.sessionIdHash, correlationId);
    }

    try {
      let previousRefreshToken: string;

      try {
        const decrypted = await this._cipher.decrypt({
          ciphertext: session.refreshToken,
          aad: {
            schemaVersion: SESSION_SCHEMA_VERSION,
            recordHash: session.sessionIdHash,
            purpose: 'refresh-token',
          },
        });

        previousRefreshToken = Buffer.from(decrypted.plaintext).toString(
          'utf8',
        );
      } catch {
        await this._deleteSessionQuietly(session.sessionIdHash);
        this._log('refresh_revoked', correlationId, 'rejected', now);

        throw unauthenticated();
      }

      const tokens = await this._oidcClient.refresh(previousRefreshToken);

      try {
        validateTokenSet(tokens);
      } catch {
        await this._revokeAndDelete(
          session.sessionIdHash,
          previousRefreshToken,
        );
        this._log('refresh_revoked', correlationId, 'rejected', now);

        throw unauthenticated();
      }

      if (equalSecrets(previousRefreshToken, tokens.refreshToken)) {
        await this._revokeAndDelete(
          session.sessionIdHash,
          previousRefreshToken,
        );
        this._log('refresh_revoked', correlationId, 'rejected', now);

        throw unauthenticated();
      }

      let refreshedIdentity: PlatformDelegatedIdentity;

      try {
        refreshedIdentity = await this._resolveAccessToken(
          tokens.accessToken,
          correlationId,
        );
      } catch {
        this._log('refresh_unavailable', correlationId, 'unavailable', now);

        if (session.accessExpiresAt > this._clock.now()) {
          return identity;
        }

        throw unavailable();
      }

      const encrypted = await this._cipher.encrypt({
        plaintext: Buffer.from(tokens.refreshToken, 'utf8'),
        aad: {
          schemaVersion: SESSION_SCHEMA_VERSION,
          recordHash: session.sessionIdHash,
          purpose: 'refresh-token',
        },
      });
      const updated = await this._store.updateSessionAfterRefresh({
        sessionIdHash: session.sessionIdHash,
        leaseId,
        subjectId: refreshedIdentity.subjectId,
        roles: refreshedIdentity.roles,
        permissions: refreshedIdentity.permissions,
        accessExpiresAt: this._clock.now() + tokens.expiresIn * 1000,
        refreshToken: encrypted,
      });

      if (!updated) {
        return this._waitForRefresh(session.sessionIdHash, correlationId);
      }

      this._log('refresh_succeeded', correlationId, 'succeeded', now);

      return refreshedIdentity;
    } catch (error: unknown) {
      if (error instanceof PlatformHttpError) {
        throw error;
      }

      if (this._oidcClient.isInvalidGrant(error)) {
        await this._deleteSessionQuietly(session.sessionIdHash);
        this._log('refresh_revoked', correlationId, 'rejected', now);

        throw unauthenticated();
      }

      this._log('refresh_unavailable', correlationId, 'unavailable', now);

      if (session.accessExpiresAt > this._clock.now()) {
        return identity;
      }

      throw unavailable();
    } finally {
      await this._releaseLeaseQuietly(session.sessionIdHash, leaseId);
    }
  }

  private async _waitForRefresh(
    sessionIdHash: string,
    correlationId: string,
  ): Promise<PlatformRequestIdentityType> {
    const deadline = this._clock.now() + REFRESH_WAIT_MS;

    while (this._clock.now() < deadline) {
      await this._clock.sleep(REFRESH_POLL_MS);

      const session = await this._store.findSession(sessionIdHash);

      if (session === undefined) {
        return new PlatformAnonymousIdentity();
      }

      if (!isSessionRecord(session) || hasExpired(session, this._clock.now())) {
        return new PlatformAnonymousIdentity();
      }

      if (session.accessExpiresAt > this._clock.now()) {
        return identityFromSession(session);
      }
    }

    this._log('refresh_unavailable', correlationId, 'unavailable', deadline);

    throw unavailable();
  }

  private async _login(
    request: IPlatformHttpRequest,
  ): Promise<IPlatformHttpResponse> {
    const startedAt = this._clock.now();

    try {
      const transactionId = randomBase64Url(32);
      const state = randomBase64Url(32);
      const nonce = randomBase64Url(32);
      const pkceVerifier = randomBase64Url(64);
      const transactionIdHash = hashOpaqueValue(transactionId);
      const replacedSessionIdHash = await this._currentSessionHash(request);
      const encryptedVerifier = await this._cipher.encrypt({
        plaintext: Buffer.from(pkceVerifier, 'utf8'),
        aad: {
          schemaVersion: SESSION_SCHEMA_VERSION,
          recordHash: transactionIdHash,
          purpose: 'pkce-verifier',
        },
      });

      await this._store.createTransaction({
        transactionIdHash,
        stateHash: hashOpaqueValue(state),
        nonce,
        expiresAt: this._clock.now() + OIDC_TRANSACTION_TTL_SECONDS * 1000,
        pkceVerifier: encryptedVerifier,
        ...(replacedSessionIdHash !== undefined && { replacedSessionIdHash }),
      });

      const location = await this._oidcClient.createAuthorizationUrl({
        state,
        nonce,
        pkceVerifier,
      });

      this._log('login_started', request.correlationId, 'succeeded', startedAt);

      return redirect(location, [transactionCookie(transactionId)]);
    } catch {
      this._log('login_failed', request.correlationId, 'failed', startedAt);

      return failedCallbackResponse();
    }
  }

  private async _callback(
    request: IPlatformHttpRequest,
  ): Promise<IPlatformHttpResponse> {
    const startedAt = this._clock.now();
    let transactionIdHash: string | undefined;
    let stateHash: string | undefined;
    let hasValidTransaction = false;

    try {
      const callback = parseCallback(request.query);
      const transactionId = parseSingleCookie(
        request.headers.cookie,
        TRANSACTION_COOKIE_NAME,
      );

      if (transactionId === undefined || !isOpaqueId(transactionId)) {
        throw new Error('Missing transaction cookie.');
      }

      transactionIdHash = hashOpaqueValue(transactionId);
      stateHash = hashOpaqueValue(callback.state);

      const transaction = await this._store.findTransaction(
        transactionIdHash,
        stateHash,
        this._clock.now(),
      );

      if (transaction === undefined || !isTransactionRecord(transaction)) {
        throw new Error('Invalid transaction.');
      }

      if (transaction.expiresAt <= this._clock.now()) {
        throw new Error('Expired transaction.');
      }

      hasValidTransaction = true;

      if (callback.kind === 'denial') {
        await this._store.consumeTransaction(transactionIdHash, stateHash);

        hasValidTransaction = false;

        this._log(
          'callback_rejected',
          request.correlationId,
          'rejected',
          startedAt,
        );

        return failedCallbackResponse();
      }

      const decrypted = await this._cipher.decrypt({
        ciphertext: transaction.pkceVerifier,
        aad: {
          schemaVersion: SESSION_SCHEMA_VERSION,
          recordHash: transactionIdHash,
          purpose: 'pkce-verifier',
        },
      });
      const pkceVerifier = Buffer.from(decrypted.plaintext).toString('utf8');

      if (!isPkceVerifier(pkceVerifier)) {
        throw new Error('Invalid PKCE verifier.');
      }

      const tokens = await this._oidcClient.exchangeAuthorizationCode({
        code: callback.code,
        state: callback.state,
        nonce: transaction.nonce,
        pkceVerifier,
      });

      validateTokenSet(tokens);

      const identity = await this._resolveAccessToken(
        tokens.accessToken,
        request.correlationId,
      );
      const sessionId = randomBase64Url(32);
      const sessionIdHash = hashOpaqueValue(sessionId);
      const encryptedRefreshToken = await this._cipher.encrypt({
        plaintext: Buffer.from(tokens.refreshToken, 'utf8'),
        aad: {
          schemaVersion: SESSION_SCHEMA_VERSION,
          recordHash: sessionIdHash,
          purpose: 'refresh-token',
        },
      });
      const now = this._clock.now();
      const saved = await this._store.createSessionFromTransaction({
        transactionIdHash,
        stateHash,
        session: {
          sessionIdHash,
          subjectId: identity.subjectId,
          roles: identity.roles,
          permissions: identity.permissions,
          createdAt: now,
          lastSeenAt: now,
          absoluteExpiresAt: now + SESSION_ABSOLUTE_TTL_MS,
          accessExpiresAt: now + tokens.expiresIn * 1000,
          refreshToken: encryptedRefreshToken,
        },
        ...(transaction.replacedSessionIdHash !== undefined && {
          replacedSessionIdHash: transaction.replacedSessionIdHash,
        }),
      });

      if (!saved) {
        throw new Error('Transaction was already consumed.');
      }

      this._log(
        'login_succeeded',
        request.correlationId,
        'succeeded',
        startedAt,
      );

      return redirect('/', [
        sessionCookie(this._config, sessionId),
        clearTransactionCookie(),
      ]);
    } catch {
      if (
        hasValidTransaction &&
        transactionIdHash !== undefined &&
        stateHash !== undefined
      ) {
        await this._consumeTransactionQuietly(transactionIdHash, stateHash);
      }

      this._log(
        'callback_rejected',
        request.correlationId,
        'rejected',
        startedAt,
      );

      return failedCallbackResponse();
    }
  }

  private async _logout(
    request: IPlatformHttpRequest,
  ): Promise<IPlatformHttpResponse> {
    const startedAt = this._clock.now();

    try {
      const sessionId = parseSingleCookie(
        request.headers.cookie,
        this._config.sessionCookieName,
      );

      if (sessionId !== undefined && isOpaqueId(sessionId)) {
        const sessionIdHash = hashOpaqueValue(sessionId);
        const session = await this._store.findSession(sessionIdHash);

        if (session !== undefined && isSessionRecord(session)) {
          try {
            const decrypted = await this._cipher.decrypt({
              ciphertext: session.refreshToken,
              aad: {
                schemaVersion: SESSION_SCHEMA_VERSION,
                recordHash: sessionIdHash,
                purpose: 'refresh-token',
              },
            });

            await this._oidcClient.revoke(
              Buffer.from(decrypted.plaintext).toString('utf8'),
            );
          } catch {
            this._log(
              'revocation_unavailable',
              request.correlationId,
              'unavailable',
              startedAt,
            );
          }

          await this._deleteSessionQuietly(sessionIdHash);
        }
      }
    } catch {
      // Logout always clears local browser state and returns 204.
    }

    this._log('logout', request.correlationId, 'succeeded', startedAt);

    return new PlatformHttpResponse({
      status: 204,
      cookies: [clearSessionCookie(this._config), clearTransactionCookie()],
    });
  }

  private async _currentSessionHash(
    request: IPlatformHttpRequest,
  ): Promise<string | undefined> {
    try {
      const sessionId = parseSingleCookie(
        request.headers.cookie,
        this._config.sessionCookieName,
      );

      if (sessionId === undefined || !isOpaqueId(sessionId)) {
        return undefined;
      }

      const hash = hashOpaqueValue(sessionId);
      const session = await this._store.findSession(hash);

      return session !== undefined &&
        isSessionRecord(session) &&
        !hasExpired(session, this._clock.now())
        ? hash
        : undefined;
    } catch {
      return undefined;
    }
  }

  private async _resolveAccessToken(
    token: string,
    correlationId: string,
  ): Promise<PlatformDelegatedIdentity> {
    const identity = await this._accessTokenResolver.resolve({
      correlationId,
      method: 'GET',
      path: '/auth/callback',
      headers: { authorization: [`Bearer ${token}`] },
      params: {},
      query: {},
    });

    if (identity.authenticationType !== 'delegated') {
      throw new Error('OIDC access token did not resolve to an identity.');
    }

    return new PlatformDelegatedIdentity({
      subjectId: identity.subjectId,
      roles: identity.roles,
      permissions: identity.permissions,
    });
  }

  private async _revokeAndDelete(
    sessionIdHash: string,
    refreshToken: string,
  ): Promise<void> {
    try {
      await this._oidcClient.revoke(refreshToken);
    } catch {
      // Confirmed local compromise is authoritative even when remote revocation fails.
    }

    await this._deleteSessionQuietly(sessionIdHash);
  }

  private async _deleteSessionQuietly(sessionIdHash: string): Promise<void> {
    try {
      await this._store.deleteSession(sessionIdHash);
    } catch {
      // Synchronous expiry still prevents identity reuse when cleanup is unavailable.
    }
  }

  private async _releaseLeaseQuietly(
    sessionIdHash: string,
    leaseId: string,
  ): Promise<void> {
    try {
      await this._store.releaseRefreshLease(sessionIdHash, leaseId);
    } catch {
      // The durable lease expires after ten seconds if release cannot be persisted.
    }
  }

  private async _consumeTransactionQuietly(
    transactionIdHash: string,
    stateHash: string,
  ): Promise<void> {
    try {
      await this._store.consumeTransaction(transactionIdHash, stateHash);
    } catch {
      // The browser cookie is still cleared even if the durable store is unavailable.
    }
  }

  private _log(
    event: IPlatformOidcSessionLogEvent['event'],
    correlationId: string,
    outcome: IPlatformOidcSessionLogEvent['outcome'],
    startedAt: number,
  ): void {
    if (this._logger === undefined) {
      return;
    }
    try {
      this._logger.log(
        Object.freeze({
          event,
          correlationId,
          outcome,
          durationMs: Math.max(0, this._clock.now() - startedAt),
        }),
      );
    } catch {
      // Authentication flow must not depend on the logging implementation.
    }
  }
}

/** @alpha */
class PlatformOidcSessionResolver implements IPlatformRequestIdentityResolver {
  constructor(private readonly _runtime: PlatformOidcSessionRuntime) {}

  resolve(
    request: IPlatformIdentityResolutionRequest,
  ): Promise<PlatformRequestIdentityType> {
    return this._runtime.resolveIdentity(request);
  }
}

class SessionRouteRegistration implements IPlatformHttpRouteRegistration {
  constructor(
    readonly method: 'GET' | 'POST',
    readonly route: string,
    private readonly _execute: (
      request: IPlatformHttpRequest,
    ) => Promise<IPlatformHttpResponse>,
  ) {}

  execute(
    input: IPlatformHttpRouteContextFactoryInput,
  ): Promise<IPlatformHttpResponse> {
    return this._execute(input.request);
  }
}
