import {
  ADMIN_AUTHENTICATION_API_SCHEMA_VERSION,
  AdminAuthenticationContractValidator,
  type AdminAuthenticationSessionResponseType,
} from '@prosto/platform-admin-contracts';
import type {
  IPlatformHttpRequest,
  IPlatformHttpResponse,
  IPlatformHttpRouteContextFactoryInput,
  IPlatformHttpRouteRegistration,
  IPlatformIdentityResolutionRequest,
  IPlatformRequestIdentityResolver,
  PlatformRequestIdentityType,
} from '@prosto/platform-sdk';
import {
  PlatformAnonymousIdentity,
  PlatformDelegatedIdentity,
  PlatformHttpResponse,
} from '@prosto/platform-sdk';
import {
  PLATFORM_LOCAL_AUTH_CSRF_COOKIE_NAME,
  PLATFORM_LOCAL_AUTH_CSRF_HEADER_NAME,
  PLATFORM_LOCAL_AUTH_CSRF_TOKEN_BYTES,
  PLATFORM_LOCAL_AUTH_SESSION_COOKIE_NAME,
  PLATFORM_LOCAL_AUTH_SESSION_TOKEN_BYTES,
} from '@/constants/index.js';
import type {
  IPlatformLocalAuthAccount,
  IPlatformLocalAuthRuntime,
  IPlatformLocalAuthRuntimeConfig,
  IPlatformLocalAuthRuntimeDependencies,
  IPlatformLocalAuthSession,
  IResolvedPlatformLocalAuthRuntimeConfig,
} from '@/interfaces/index.js';
import {
  createExpiredPlatformLocalAuthCookie,
  createPlatformLocalAuthCookie,
  equalPlatformLocalAuthSecrets,
  hashOpaqueValue,
  isOpaquePlatformLocalAuthToken,
  normalizePlatformLocalAuthUsername,
  parsePlatformLocalAuthCookie,
  resolvePlatformLocalAuthConfig,
} from '@/utils/index.js';

/**
 * @alpha
 * Creates a framework-neutral local authentication runtime. Persistence,
 * timing, random values, password hashing and limiting are supplied by ports.
 */
export function createPlatformLocalAuthRuntime(
  config: IPlatformLocalAuthRuntimeConfig,
  dependencies: IPlatformLocalAuthRuntimeDependencies,
): IPlatformLocalAuthRuntime {
  const runtime = new PlatformLocalAuthRuntime(
    resolvePlatformLocalAuthConfig(config),
    dependencies,
  );

  return Object.freeze({
    resolver: runtime.resolver,
    routes: runtime.routes,
  });
}

class PlatformLocalAuthRuntime {
  readonly resolver: IPlatformRequestIdentityResolver;
  readonly routes: readonly IPlatformHttpRouteRegistration[];
  private readonly _validator = new AdminAuthenticationContractValidator();

  constructor(
    private readonly _config: IResolvedPlatformLocalAuthRuntimeConfig,
    private readonly _dependencies: IPlatformLocalAuthRuntimeDependencies,
  ) {
    this.resolver = new PlatformLocalAuthResolver(this);
    this.routes = Object.freeze([
      new LocalAuthRouteRegistration(
        'GET',
        '/admin/api/v1/auth/session',
        (request) => this._sessionStatus(request),
      ),
      new LocalAuthRouteRegistration(
        'POST',
        '/admin/api/v1/auth/login',
        (request) => this._login(request),
      ),
      new LocalAuthRouteRegistration(
        'POST',
        '/admin/api/v1/auth/change-password',
        (request) => this._changePassword(request),
      ),
      new LocalAuthRouteRegistration(
        'POST',
        '/admin/api/v1/auth/logout',
        (request) => this._logout(request),
      ),
    ]);
  }

  async resolveIdentity(
    request: IPlatformIdentityResolutionRequest,
  ): Promise<PlatformRequestIdentityType> {
    const active = await this._findActiveSession(request.headers.cookie);

    if (active === undefined || active.account.mustChangePassword) {
      return new PlatformAnonymousIdentity();
    }

    await this._touch(active.session);

    return new PlatformDelegatedIdentity({
      subjectId: active.account.id,
      roles: active.account.roles,
      permissions: active.account.permissions,
    });
  }

  private async _sessionStatus(
    request: IPlatformHttpRequest,
  ): Promise<IPlatformHttpResponse> {
    const active = await this._findActiveSession(request.headers.cookie);

    if (active === undefined) {
      const csrfToken = this._randomToken(PLATFORM_LOCAL_AUTH_CSRF_TOKEN_BYTES);

      return this._response(
        200,
        {
          mode: 'local',
          state: 'anonymous',
          schemaVersion: ADMIN_AUTHENTICATION_API_SCHEMA_VERSION,
        },
        [this._csrfCookie(csrfToken)],
      );
    }

    const csrfToken = this._randomToken(PLATFORM_LOCAL_AUTH_CSRF_TOKEN_BYTES);
    const rotated = await this._dependencies.store.rotateSessionCsrfToken({
      sessionTokenHash: active.session.sessionTokenHash,
      csrfTokenHash: hashOpaqueValue(csrfToken),
    });

    if (!rotated) {
      return this._failure(503, 'AUTHENTICATION_UNAVAILABLE');
    }

    await this._touch(active.session);

    return this._response(
      200,
      {
        mode: 'local',
        state: active.account.mustChangePassword
          ? 'password-change-required'
          : 'authenticated',
        schemaVersion: ADMIN_AUTHENTICATION_API_SCHEMA_VERSION,
      },
      [this._csrfCookie(csrfToken)],
    );
  }

  private async _login(
    request: IPlatformHttpRequest,
  ): Promise<IPlatformHttpResponse> {
    if (
      !this._hasExpectedOrigin(request) ||
      !this._hasDoubleSubmitCsrf(request)
    ) {
      return this._failure(400, 'AUTHENTICATION_FAILED');
    }

    let body: { readonly username: string; readonly password: string };

    try {
      body = this._parseJson(request, (payload) =>
        this._validator.parseLoginRequest(payload),
      );
    } catch {
      return this._failure(400, 'AUTHENTICATION_FAILED');
    }

    const startedAt = this._dependencies.clock.now();
    const username = normalizePlatformLocalAuthUsername(body.username);

    try {
      const blocked = await this._dependencies.limiter.isBlocked(
        username,
        startedAt,
      );
      const account =
        await this._dependencies.store.findAccountByUsername(username);
      let validPassword = false;

      if (account) {
        validPassword = await this._dependencies.passwordHasher.verify(
          account.passwordHash,
          body.password,
        );
      } else {
        await this._dependencies.passwordHasher.verifyUnknown(body.password);
      }

      const allowed =
        !blocked &&
        account !== undefined &&
        account.disabledAt === undefined &&
        (account.lockoutUntil === undefined ||
          account.lockoutUntil <= startedAt) &&
        validPassword;

      if (!allowed || !account) {
        await this._dependencies.limiter.recordFailure(username, startedAt);

        this._log(
          'login_rejected',
          request.correlationId,
          'rejected',
          startedAt,
        );

        return this._failure(401, 'AUTHENTICATION_FAILED');
      }

      await this._dependencies.limiter.clearFailures(username);

      if (this._dependencies.passwordHasher.needsRehash(account.passwordHash)) {
        const passwordHash = await this._dependencies.passwordHasher.hash(
          body.password,
        );
        const updated = await this._dependencies.store.updateAccountPassword({
          accountId: account.id,
          passwordHash,
          mustChangePassword: account.mustChangePassword,
        });

        if (!updated) {
          return this._failure(503, 'AUTHENTICATION_UNAVAILABLE');
        }
      }

      const issued = await this._issueSession(account);

      this._log(
        'login_succeeded',
        request.correlationId,
        'succeeded',
        startedAt,
      );

      return this._response(
        200,
        {
          mode: 'local',
          state: account.mustChangePassword
            ? 'password-change-required'
            : 'authenticated',
          schemaVersion: ADMIN_AUTHENTICATION_API_SCHEMA_VERSION,
        },
        issued.cookies,
      );
    } catch {
      this._log(
        'login_rejected',
        request.correlationId,
        'unavailable',
        startedAt,
      );

      return this._failure(503, 'AUTHENTICATION_UNAVAILABLE');
    }
  }

  private async _changePassword(
    request: IPlatformHttpRequest,
  ): Promise<IPlatformHttpResponse> {
    if (
      !this._hasExpectedOrigin(request) ||
      !this._hasDoubleSubmitCsrf(request)
    ) {
      return this._failure(400, 'AUTHENTICATION_FAILED');
    }

    let body: {
      readonly currentPassword: string;
      readonly newPassword: string;
    };

    try {
      body = this._parseJson(request, (payload) =>
        this._validator.parseChangePasswordRequest(payload),
      );
    } catch {
      return this._failure(400, 'AUTHENTICATION_FAILED');
    }

    if (body.newPassword.length < this._config.minimumPasswordLength) {
      return this._failure(400, 'AUTHENTICATION_FAILED');
    }

    const active = await this._findActiveSession(request.headers.cookie);

    if (!active || !this._matchesSessionCsrf(active.session, request)) {
      return this._failure(401, 'AUTHENTICATION_FAILED');
    }

    try {
      const validPassword = await this._dependencies.passwordHasher.verify(
        active.account.passwordHash,
        body.currentPassword,
      );

      if (!validPassword) {
        return this._failure(401, 'AUTHENTICATION_FAILED');
      }

      const passwordHash = await this._dependencies.passwordHasher.hash(
        body.newPassword,
      );
      const updated = await this._dependencies.store.updateAccountPassword({
        accountId: active.account.id,
        passwordHash,
        mustChangePassword: false,
      });

      if (!updated) {
        return this._failure(503, 'AUTHENTICATION_UNAVAILABLE');
      }

      const issued = await this._issueSession({
        ...active.account,
        passwordHash,
        mustChangePassword: false,
      });

      this._log(
        'password_changed',
        request.correlationId,
        'succeeded',
        this._dependencies.clock.now(),
      );

      return this._response(
        200,
        {
          mode: 'local',
          state: 'authenticated',
          schemaVersion: ADMIN_AUTHENTICATION_API_SCHEMA_VERSION,
        },
        issued.cookies,
      );
    } catch {
      return this._failure(503, 'AUTHENTICATION_UNAVAILABLE');
    }
  }

  private async _logout(
    request: IPlatformHttpRequest,
  ): Promise<IPlatformHttpResponse> {
    if (
      !this._hasExpectedOrigin(request) ||
      !this._hasDoubleSubmitCsrf(request)
    ) {
      return this._failure(400, 'AUTHENTICATION_FAILED');
    }

    try {
      this._parseJson(request, (payload) =>
        this._validator.parseLogoutRequest(payload),
      );
    } catch {
      return this._failure(400, 'AUTHENTICATION_FAILED');
    }

    const active = await this._findActiveSession(request.headers.cookie);

    if (!active || !this._matchesSessionCsrf(active.session, request)) {
      return this._failure(401, 'AUTHENTICATION_FAILED');
    }

    await this._dependencies.store.deleteSession(
      active.session.sessionTokenHash,
    );

    this._log(
      'logout',
      request.correlationId,
      'succeeded',
      this._dependencies.clock.now(),
    );

    return this._response(
      200,
      {
        mode: 'local',
        state: 'anonymous',
        schemaVersion: ADMIN_AUTHENTICATION_API_SCHEMA_VERSION,
      },
      [
        createExpiredPlatformLocalAuthCookie(
          PLATFORM_LOCAL_AUTH_SESSION_COOKIE_NAME,
          true,
          this._config.secureCookies,
          '/admin',
        ),
        createExpiredPlatformLocalAuthCookie(
          PLATFORM_LOCAL_AUTH_CSRF_COOKIE_NAME,
          false,
          this._config.secureCookies,
        ),
      ],
    );
  }

  private async _findActiveSession(
    cookieHeaders: readonly string[] | undefined,
  ): Promise<
    | {
        readonly session: IPlatformLocalAuthSession;
        readonly account: IPlatformLocalAuthAccount;
      }
    | undefined
  > {
    const token = parsePlatformLocalAuthCookie(
      cookieHeaders,
      PLATFORM_LOCAL_AUTH_SESSION_COOKIE_NAME,
    );

    if (!token || !isOpaquePlatformLocalAuthToken(token)) {
      return undefined;
    }

    const sessionTokenHash = hashOpaqueValue(token);
    const session =
      await this._dependencies.store.findSession(sessionTokenHash);
    const now = this._dependencies.clock.now();

    if (
      !session ||
      !this._isValidSession(session) ||
      session.sessionTokenHash !== sessionTokenHash ||
      now >= session.idleExpiresAt ||
      now >= session.absoluteExpiresAt
    ) {
      if (session) {
        try {
          await this._dependencies.store.deleteSession(sessionTokenHash);
        } catch {
          // Expiry is authoritative even when opportunistic cleanup fails.
        }
      }

      return undefined;
    }

    const account = await this._dependencies.store.findAccountById(
      session.accountId,
    );

    if (
      !account ||
      !this._isValidAccount(account) ||
      account.disabledAt !== undefined ||
      (account.lockoutUntil !== undefined && account.lockoutUntil > now)
    ) {
      return undefined;
    }

    return { session, account };
  }

  private async _issueSession(account: IPlatformLocalAuthAccount): Promise<{
    readonly cookies: readonly ReturnType<
      typeof createPlatformLocalAuthCookie
    >[];
  }> {
    const sessionToken = this._randomToken(
      PLATFORM_LOCAL_AUTH_SESSION_TOKEN_BYTES,
    );
    const csrfToken = this._randomToken(PLATFORM_LOCAL_AUTH_CSRF_TOKEN_BYTES);
    const now = this._dependencies.clock.now();
    const session: IPlatformLocalAuthSession = {
      sessionTokenHash: hashOpaqueValue(sessionToken),
      accountId: account.id,
      csrfTokenHash: hashOpaqueValue(csrfToken),
      createdAt: now,
      lastSeenAt: now,
      idleExpiresAt: Math.min(
        now + this._config.sessionIdleTtlMs,
        now + this._config.sessionAbsoluteTtlMs,
      ),
      absoluteExpiresAt: now + this._config.sessionAbsoluteTtlMs,
    };

    await this._dependencies.store.replaceAccountSessions({
      accountId: account.id,
      session,
    });

    return {
      cookies: [
        createPlatformLocalAuthCookie(
          PLATFORM_LOCAL_AUTH_SESSION_COOKIE_NAME,
          sessionToken,
          true,
          this._config.secureCookies,
          '/admin',
        ),
        this._csrfCookie(csrfToken),
      ],
    };
  }

  private _hasExpectedOrigin(request: IPlatformHttpRequest): boolean {
    const origin = request.headers.origin;
    return origin?.length === 1 && origin[0] === this._config.origin;
  }

  private _hasDoubleSubmitCsrf(request: IPlatformHttpRequest): boolean {
    const csrfCookie = parsePlatformLocalAuthCookie(
      request.headers.cookie,
      PLATFORM_LOCAL_AUTH_CSRF_COOKIE_NAME,
    );
    const csrfHeader = request.headers[PLATFORM_LOCAL_AUTH_CSRF_HEADER_NAME];

    return (
      csrfCookie !== undefined &&
      csrfHeader?.length === 1 &&
      equalPlatformLocalAuthSecrets(csrfCookie, csrfHeader[0] ?? '')
    );
  }

  private _matchesSessionCsrf(
    session: IPlatformLocalAuthSession,
    request: IPlatformHttpRequest,
  ): boolean {
    const csrfCookie = parsePlatformLocalAuthCookie(
      request.headers.cookie,
      PLATFORM_LOCAL_AUTH_CSRF_COOKIE_NAME,
    );

    return (
      csrfCookie !== undefined &&
      hashOpaqueValue(csrfCookie) === session.csrfTokenHash
    );
  }

  private _parseJson<T>(
    request: IPlatformHttpRequest,
    parse: (payload: unknown) => T,
  ): T {
    if (request.body.variant !== 'json') {
      throw new Error('Expected JSON request body.');
    }

    return parse(request.body.data);
  }

  private async _touch(session: IPlatformLocalAuthSession): Promise<void> {
    const now = this._dependencies.clock.now();

    await this._dependencies.store.touchSession({
      sessionTokenHash: session.sessionTokenHash,
      lastSeenAt: now,
      idleExpiresAt: Math.min(
        now + this._config.sessionIdleTtlMs,
        session.absoluteExpiresAt,
      ),
    });
  }

  private _csrfCookie(
    token: string,
  ): ReturnType<typeof createPlatformLocalAuthCookie> {
    return createPlatformLocalAuthCookie(
      PLATFORM_LOCAL_AUTH_CSRF_COOKIE_NAME,
      token,
      false,
      this._config.secureCookies,
    );
  }

  private _randomToken(bytes: number): string {
    const token = this._dependencies.randomness.base64Url(bytes);

    if (!isOpaquePlatformLocalAuthToken(token)) {
      throw new Error('Randomness port returned an invalid opaque token.');
    }

    return token;
  }

  private _isValidSession(value: IPlatformLocalAuthSession): boolean {
    return (
      isOpaquePlatformLocalAuthToken(value.sessionTokenHash) &&
      isOpaquePlatformLocalAuthToken(value.csrfTokenHash) &&
      typeof value.accountId === 'string' &&
      value.accountId.trim().length > 0 &&
      Number.isSafeInteger(value.createdAt) &&
      Number.isSafeInteger(value.lastSeenAt) &&
      Number.isSafeInteger(value.idleExpiresAt) &&
      Number.isSafeInteger(value.absoluteExpiresAt) &&
      value.createdAt <= value.lastSeenAt &&
      value.lastSeenAt <= value.idleExpiresAt &&
      value.idleExpiresAt <= value.absoluteExpiresAt
    );
  }

  private _isValidAccount(value: IPlatformLocalAuthAccount): boolean {
    return (
      typeof value.id === 'string' &&
      value.id.trim().length > 0 &&
      typeof value.username === 'string' &&
      normalizePlatformLocalAuthUsername(value.username) === value.username &&
      typeof value.passwordHash === 'string' &&
      value.passwordHash.length > 0 &&
      [value.roles, value.permissions].every(
        (entries) =>
          Array.isArray(entries) &&
          entries.every(
            (entry) =>
              typeof entry === 'string' &&
              entry.trim().length > 0 &&
              entry.length <= 255,
          ),
      ) &&
      (value.disabledAt === undefined ||
        Number.isSafeInteger(value.disabledAt)) &&
      (value.lockoutUntil === undefined ||
        Number.isSafeInteger(value.lockoutUntil))
    );
  }

  private _response(
    status: number,
    payload: AdminAuthenticationSessionResponseType | Record<string, unknown>,
    cookies?: readonly ReturnType<typeof createPlatformLocalAuthCookie>[],
  ): IPlatformHttpResponse {
    return new PlatformHttpResponse({
      status,
      body: { variant: 'json', data: payload },
      cookies,
    });
  }

  private _failure(
    status: number,
    code: 'AUTHENTICATION_FAILED' | 'AUTHENTICATION_UNAVAILABLE',
  ): IPlatformHttpResponse {
    return this._response(status, {
      schemaVersion: ADMIN_AUTHENTICATION_API_SCHEMA_VERSION,
      code,
    });
  }

  private _log(
    event: 'login_succeeded' | 'login_rejected' | 'password_changed' | 'logout',
    correlationId: string,
    outcome: 'succeeded' | 'rejected' | 'unavailable',
    startedAt: number,
  ): void {
    try {
      this._dependencies.logger?.log({
        event,
        correlationId,
        outcome,
        durationMs: Math.max(0, this._dependencies.clock.now() - startedAt),
      });
    } catch {
      // Authentication correctness cannot depend on telemetry delivery.
    }
  }
}

class PlatformLocalAuthResolver implements IPlatformRequestIdentityResolver {
  constructor(private readonly _runtime: PlatformLocalAuthRuntime) {}

  resolve(
    request: IPlatformIdentityResolutionRequest,
  ): Promise<PlatformRequestIdentityType> {
    return this._runtime.resolveIdentity(request);
  }
}

class LocalAuthRouteRegistration implements IPlatformHttpRouteRegistration {
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
