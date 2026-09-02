import type {
  IPlatformHttpRequest,
  IPlatformHttpRouteRegistration,
  IPlatformIdentityResolutionRequest,
} from '@prosto/platform-sdk';
import { PlatformAnonymousIdentity } from '@prosto/platform-sdk';
import type {
  IPlatformLocalAuthAccount,
  IPlatformLocalAuthFailedLoginLimiter,
  IPlatformLocalAuthPasswordHasher,
  IPlatformLocalAuthSession,
  IPlatformLocalAuthSessionStore,
} from '@/index.js';
import {
  PLATFORM_LOCAL_AUTH_CSRF_COOKIE_NAME,
  PLATFORM_LOCAL_AUTH_CSRF_HEADER_NAME,
  PLATFORM_LOCAL_AUTH_SESSION_COOKIE_NAME,
  createPlatformLocalAuthenticationProvider,
  createPlatformLocalAuthRuntime,
  equalPlatformLocalAuthSecrets,
  hashOpaqueValue,
  parsePlatformLocalAuthCookie,
} from '@/index.js';
import { describe, expect, it, vi } from 'vitest';

const ORIGIN = 'http://127.0.0.1:3001';
const NOW = 1_700_000_000_000;

class MemoryStore implements IPlatformLocalAuthSessionStore {
  readonly accounts = new Map<string, IPlatformLocalAuthAccount>();
  readonly usernames = new Map<string, string>();
  readonly sessions = new Map<string, IPlatformLocalAuthSession>();

  addAccount(record: IPlatformLocalAuthAccount): void {
    this.accounts.set(record.id, record);
    this.usernames.set(record.username, record.id);
  }

  async findAccountByUsername(
    username: string,
  ): Promise<IPlatformLocalAuthAccount | undefined> {
    const id = this.usernames.get(username);
    return id === undefined ? undefined : this.accounts.get(id);
  }

  async findAccountById(
    id: string,
  ): Promise<IPlatformLocalAuthAccount | undefined> {
    return this.accounts.get(id);
  }

  async updateAccountPassword(input: {
    readonly accountId: string;
    readonly passwordHash: string;
    readonly mustChangePassword: boolean;
  }): Promise<boolean> {
    const existingAccount = this.accounts.get(input.accountId);

    if (existingAccount === undefined) {
      return false;
    }

    this.accounts.set(input.accountId, {
      ...existingAccount,
      passwordHash: input.passwordHash,
      mustChangePassword: input.mustChangePassword,
    });

    return true;
  }

  async findSession(
    hash: string,
  ): Promise<IPlatformLocalAuthSession | undefined> {
    return this.sessions.get(hash);
  }

  async touchSession(input: {
    readonly sessionTokenHash: string;
    readonly lastSeenAt: number;
    readonly idleExpiresAt: number;
  }): Promise<void> {
    const session = this.sessions.get(input.sessionTokenHash);

    if (session !== undefined) {
      this.sessions.set(input.sessionTokenHash, { ...session, ...input });
    }
  }

  async rotateSessionCsrfToken(input: {
    readonly sessionTokenHash: string;
    readonly csrfTokenHash: string;
  }): Promise<boolean> {
    const session = this.sessions.get(input.sessionTokenHash);

    if (session === undefined) {
      return false;
    }

    this.sessions.set(input.sessionTokenHash, { ...session, ...input });

    return true;
  }

  async deleteSession(hash: string): Promise<void> {
    this.sessions.delete(hash);
  }

  async replaceAccountSessions(input: {
    readonly accountId: string;
    readonly session: IPlatformLocalAuthSession;
  }): Promise<void> {
    for (const [hash, session] of this.sessions) {
      if (session.accountId === input.accountId) {
        this.sessions.delete(hash);
      }
    }

    this.sessions.set(input.session.sessionTokenHash, input.session);
  }
}

function account(
  overrides: Partial<IPlatformLocalAuthAccount> = {},
): IPlatformLocalAuthAccount {
  return {
    id: 'account-42',
    username: 'admin',
    passwordHash: 'hash:old-password',
    roles: ['admin'],
    permissions: ['plugins:read'],
    mustChangePassword: false,
    ...overrides,
  };
}

function createHarness(
  overrides: {
    readonly mustChangePassword?: boolean;
    readonly blocked?: boolean;
  } = {},
) {
  let now = NOW;
  let token = 0;

  const store = new MemoryStore();

  store.addAccount(
    account({ mustChangePassword: overrides.mustChangePassword ?? false }),
  );

  const hasher: IPlatformLocalAuthPasswordHasher = {
    hash: vi.fn(
      async (password: string): Promise<string> => `hash:${password}`,
    ),
    verify: vi.fn(
      async (hash: string, password: string): Promise<boolean> =>
        hash === `hash:${password}`,
    ),
    verifyUnknown: vi.fn(async (): Promise<void> => undefined),
    needsRehash: (): boolean => false,
  };
  const limiter: IPlatformLocalAuthFailedLoginLimiter = {
    isBlocked: vi.fn(async (): Promise<boolean> => overrides.blocked ?? false),
    recordFailure: vi.fn(async (): Promise<void> => undefined),
    clearFailures: vi.fn(async (): Promise<void> => undefined),
  };
  const runtime = createPlatformLocalAuthRuntime(
    {
      origin: ORIGIN,
      secureCookies: false,
      sessionIdleTtlMs: 1000,
      sessionAbsoluteTtlMs: 5000,
    },
    {
      store,
      passwordHasher: hasher,
      limiter,
      clock: { now: (): number => now },
      randomness: {
        base64Url: (): string => `${(++token).toString().padStart(43, 'a')}`,
      },
    },
  );
  return {
    runtime,
    store,
    hasher,
    limiter,
    advance: (milliseconds: number): void => {
      now += milliseconds;
    },
  };
}

function request(
  overrides: Partial<IPlatformHttpRequest> = {},
): IPlatformHttpRequest {
  return {
    method: 'GET',
    path: '/admin/api/v1/auth/session',
    params: {},
    query: {},
    headers: {},
    body: { variant: 'empty' },
    correlationId: 'correlation-42',
    identity: new PlatformAnonymousIdentity(),
    ...overrides,
  };
}

async function execute(
  registration: IPlatformHttpRouteRegistration,
  input: IPlatformHttpRequest,
) {
  return registration.execute({
    request: input,
    baseContext: {
      correlationId: input.correlationId,
      identity: input.identity,
      signal: new AbortController().signal,
    },
  });
}

function route(
  runtime: ReturnType<typeof createPlatformLocalAuthRuntime>,
  path: string,
): IPlatformHttpRouteRegistration {
  const registration = runtime.routes.find((entry) => entry.route === path);

  if (registration === undefined) {
    throw new Error(`Route ${path} is missing.`);
  }

  return registration;
}

function cookie(
  response: Awaited<ReturnType<typeof execute>>,
  name: string,
): string {
  const value = response.cookies?.find((entry) => entry.name === name)?.value;

  if (value === undefined) {
    throw new Error(`Cookie ${name} is missing.`);
  }

  return value;
}

function identityRequest(token: string): IPlatformIdentityResolutionRequest {
  return {
    correlationId: 'correlation-42',
    method: 'GET',
    path: '/admin/plugins',
    headers: {
      cookie: [`${PLATFORM_LOCAL_AUTH_SESSION_COOKIE_NAME}=${token}`],
    },
    params: {},
    query: {},
  };
}

describe('local authentication runtime', (): void => {
  it('adapts local routes and resolver to the generic provider facade', (): void => {
    const { runtime } = createHarness();

    const provider = createPlatformLocalAuthenticationProvider(runtime);

    expect(provider).toMatchObject({
      mode: 'local',
      resolver: runtime.resolver,
    });
    expect(provider.publicRouteRegistrations).toBe(runtime.routes);
  });

  it('rotates the session after a forced password change and resolves stored delegated identity', async (): Promise<void> => {
    const { runtime, store } = createHarness({ mustChangePassword: true });
    const status = await execute(
      route(runtime, '/admin/api/v1/auth/session'),
      request(),
    );
    const preLoginCsrf = cookie(status, PLATFORM_LOCAL_AUTH_CSRF_COOKIE_NAME);
    const login = await execute(
      route(runtime, '/admin/api/v1/auth/login'),
      request({
        method: 'POST',
        path: '/admin/api/v1/auth/login',
        headers: {
          origin: [ORIGIN],
          cookie: [`${PLATFORM_LOCAL_AUTH_CSRF_COOKIE_NAME}=${preLoginCsrf}`],
          [PLATFORM_LOCAL_AUTH_CSRF_HEADER_NAME]: [preLoginCsrf],
        },
        body: {
          variant: 'json',
          data: {
            schemaVersion: 'admin-authentication-api.v1',
            username: 'ADMIN',
            password: 'old-password',
          },
        },
      }),
    );
    expect(login.status).toBe(200);

    const oldSession = cookie(login, PLATFORM_LOCAL_AUTH_SESSION_COOKIE_NAME);
    const oldCsrf = cookie(login, PLATFORM_LOCAL_AUTH_CSRF_COOKIE_NAME);

    expect(login.body).toMatchObject({
      data: { state: 'password-change-required' },
    });
    expect(
      (await runtime.resolver.resolve(identityRequest(oldSession)))
        .authenticationType,
    ).toBe('anonymous');

    const changed = await execute(
      route(runtime, '/admin/api/v1/auth/change-password'),
      request({
        method: 'POST',
        path: '/admin/api/v1/auth/change-password',
        headers: {
          origin: [ORIGIN],
          cookie: [
            `${PLATFORM_LOCAL_AUTH_SESSION_COOKIE_NAME}=${oldSession}; ${PLATFORM_LOCAL_AUTH_CSRF_COOKIE_NAME}=${oldCsrf}`,
          ],
          [PLATFORM_LOCAL_AUTH_CSRF_HEADER_NAME]: [oldCsrf],
        },
        body: {
          variant: 'json',
          data: {
            schemaVersion: 'admin-authentication-api.v1',
            currentPassword: 'old-password',
            newPassword: 'new-strong-password',
          },
        },
      }),
    );
    const newSession = cookie(changed, PLATFORM_LOCAL_AUTH_SESSION_COOKIE_NAME);

    expect(changed.body).toMatchObject({ data: { state: 'authenticated' } });
    expect(store.sessions.has(hashOpaqueValue(oldSession))).toBe(false);
    await expect(
      runtime.resolver.resolve(identityRequest(oldSession)),
    ).resolves.toMatchObject({ authenticationType: 'anonymous' });
    await expect(
      runtime.resolver.resolve(identityRequest(newSession)),
    ).resolves.toMatchObject({
      authenticationType: 'delegated',
      subjectId: 'account-42',
      roles: ['admin'],
      permissions: ['plugins:read'],
    });
  });

  it('uses the same generic response for missing and invalid accounts while running unknown-password verification', async (): Promise<void> => {
    const { runtime, hasher, limiter } = createHarness();
    const status = await execute(
      route(runtime, '/admin/api/v1/auth/session'),
      request(),
    );
    const csrf = cookie(status, PLATFORM_LOCAL_AUTH_CSRF_COOKIE_NAME);
    const login = async (username: string, password: string) =>
      execute(
        route(runtime, '/admin/api/v1/auth/login'),
        request({
          method: 'POST',
          path: '/admin/api/v1/auth/login',
          headers: {
            origin: [ORIGIN],
            cookie: [`${PLATFORM_LOCAL_AUTH_CSRF_COOKIE_NAME}=${csrf}`],
            [PLATFORM_LOCAL_AUTH_CSRF_HEADER_NAME]: [csrf],
          },
          body: {
            variant: 'json',
            data: {
              schemaVersion: 'admin-authentication-api.v1',
              username,
              password,
            },
          },
        }),
      );

    const absent = await login('missing', 'wrong-password');
    const invalid = await login('admin', 'wrong-password');

    expect(absent.status).toBe(401);
    expect(invalid.status).toBe(401);
    expect(absent.body).toEqual(invalid.body);
    expect(hasher.verifyUnknown).toHaveBeenCalledOnce();
    expect(limiter.recordFailure).toHaveBeenCalledTimes(2);
  });

  it('rejects malformed bodies, cross-origin requests and invalid CSRF before credentials are used', async (): Promise<void> => {
    const { runtime, hasher } = createHarness();
    const malformed = await execute(
      route(runtime, '/admin/api/v1/auth/login'),
      request({
        method: 'POST',
        path: '/admin/api/v1/auth/login',
        headers: { origin: [ORIGIN] },
        body: { variant: 'json', data: { username: 'admin' } },
      }),
    );
    const foreign = await execute(
      route(runtime, '/admin/api/v1/auth/login'),
      request({
        method: 'POST',
        path: '/admin/api/v1/auth/login',
        headers: { origin: ['https://attacker.test'] },
        body: { variant: 'json', data: {} },
      }),
    );

    expect(malformed.status).toBe(400);
    expect(foreign.status).toBe(400);
    expect(hasher.verify).not.toHaveBeenCalled();
  });

  it('returns anonymous identity and removes an expired session', async (): Promise<void> => {
    const { runtime, store, advance } = createHarness();
    const token = 'x'.repeat(43);
    store.sessions.set(hashOpaqueValue(token), {
      sessionTokenHash: hashOpaqueValue(token),
      accountId: 'account-42',
      csrfTokenHash: hashOpaqueValue('y'.repeat(43)),
      createdAt: NOW,
      lastSeenAt: NOW,
      idleExpiresAt: NOW + 1000,
      absoluteExpiresAt: NOW + 5000,
    });
    advance(1000);

    await expect(
      runtime.resolver.resolve(identityRequest(token)),
    ).resolves.toMatchObject({ authenticationType: 'anonymous' });
    expect(store.sessions.has(hashOpaqueValue(token))).toBe(false);
    await expect(
      runtime.resolver.resolve(identityRequest('not-an-opaque-session')),
    ).resolves.toMatchObject({ authenticationType: 'anonymous' });
  });

  it('does not issue a session when the failed-login limiter blocks the account name', async (): Promise<void> => {
    const { runtime, limiter } = createHarness({ blocked: true });
    const status = await execute(
      route(runtime, '/admin/api/v1/auth/session'),
      request(),
    );
    const csrf = cookie(status, PLATFORM_LOCAL_AUTH_CSRF_COOKIE_NAME);

    const response = await execute(
      route(runtime, '/admin/api/v1/auth/login'),
      request({
        method: 'POST',
        path: '/admin/api/v1/auth/login',
        headers: {
          origin: [ORIGIN],
          cookie: [`${PLATFORM_LOCAL_AUTH_CSRF_COOKIE_NAME}=${csrf}`],
          [PLATFORM_LOCAL_AUTH_CSRF_HEADER_NAME]: [csrf],
        },
        body: {
          variant: 'json',
          data: {
            schemaVersion: 'admin-authentication-api.v1',
            username: 'admin',
            password: 'old-password',
          },
        },
      }),
    );

    expect(response.status).toBe(401);
    expect(response.cookies).toBeUndefined();
    expect(limiter.recordFailure).toHaveBeenCalledOnce();
  });

  it('parses only a single cookie and compares matching secrets in constant time', (): void => {
    expect(
      parsePlatformLocalAuthCookie(['session=one; session=two'], 'session'),
    ).toBeUndefined();
    expect(parsePlatformLocalAuthCookie(['session=one'], 'session')).toBe(
      'one',
    );
    expect(equalPlatformLocalAuthSecrets('value', 'value')).toBe(true);
    expect(equalPlatformLocalAuthSecrets('value', 'other')).toBe(false);
  });
});
