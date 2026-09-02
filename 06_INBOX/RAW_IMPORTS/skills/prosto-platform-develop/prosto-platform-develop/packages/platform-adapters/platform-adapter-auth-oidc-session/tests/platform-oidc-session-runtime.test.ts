import { createHash } from 'node:crypto';
import type {
  IPlatformHttpRequest,
  IPlatformHttpRouteRegistration,
  IPlatformSecretCipher,
  IPlatformSecretCiphertext,
  PlatformRequestIdentityType,
} from '@prosto/platform-sdk';
import {
  PlatformAnonymousIdentity,
  PlatformDelegatedIdentity,
} from '@prosto/platform-sdk';
import {
  type IPlatformOidcClientFacade,
  type IPlatformOidcSessionClock,
  type IPlatformOidcSessionRecord,
  type IPlatformOidcSessionStore,
  type IPlatformOidcTransactionRecord,
  createPlatformOidcAuthenticationProvider,
  createPlatformOidcSessionRuntime,
} from '@/index.js';
import { describe, expect, it, vi } from 'vitest';

const NOW = 1_700_000_000_000;

function hash(value: string): string {
  return createHash('sha256').update(value, 'utf8').digest('base64url');
}

function createClock(): IPlatformOidcSessionClock {
  let now = NOW;
  return {
    now: (): number => now,
    sleep: async (milliseconds: number): Promise<void> => {
      now += milliseconds;
    },
  };
}

function createCipher(): IPlatformSecretCipher {
  return {
    encrypt: async ({ plaintext }): Promise<IPlatformSecretCiphertext> => ({
      keyId: 'test',
      nonce: 'a'.repeat(16),
      tag: 'b'.repeat(22),
      ciphertext: Buffer.from(plaintext).toString('base64url'),
    }),
    decrypt: async ({ ciphertext }) => ({
      plaintext: new Uint8Array(
        Buffer.from(ciphertext.ciphertext, 'base64url'),
      ),
      requiresReencryption: false,
    }),
  };
}

class MemoryStore implements IPlatformOidcSessionStore {
  readonly sessions = new Map<string, IPlatformOidcSessionRecord>();
  readonly transactions = new Map<string, IPlatformOidcTransactionRecord>();

  async findSession(
    sessionIdHash: string,
  ): Promise<IPlatformOidcSessionRecord | undefined> {
    return this.sessions.get(sessionIdHash);
  }

  async touchSession(sessionIdHash: string, now: number): Promise<void> {
    const session = this.sessions.get(sessionIdHash);

    if (session !== undefined) {
      this.sessions.set(sessionIdHash, { ...session, lastSeenAt: now });
    }
  }

  async deleteSession(sessionIdHash: string): Promise<void> {
    this.sessions.delete(sessionIdHash);
  }

  async createTransaction(
    record: IPlatformOidcTransactionRecord,
  ): Promise<void> {
    this.transactions.set(record.transactionIdHash, record);
  }

  async findTransaction(
    transactionIdHash: string,
    stateHash: string,
    _now: number,
  ): Promise<IPlatformOidcTransactionRecord | undefined> {
    const transaction = this.transactions.get(transactionIdHash);

    return transaction?.stateHash === stateHash ? transaction : undefined;
  }

  async consumeTransaction(
    transactionIdHash: string,
    stateHash: string,
  ): Promise<void> {
    const transaction = await this.findTransaction(
      transactionIdHash,
      stateHash,
      NOW,
    );

    if (transaction !== undefined) {
      this.transactions.delete(transactionIdHash);
    }
  }

  async createSessionFromTransaction(input: {
    readonly transactionIdHash: string;
    readonly stateHash: string;
    readonly session: IPlatformOidcSessionRecord;
    readonly replacedSessionIdHash?: string;
  }): Promise<boolean> {
    const transaction = await this.findTransaction(
      input.transactionIdHash,
      input.stateHash,
      NOW,
    );

    if (transaction === undefined) {
      return false;
    }

    this.transactions.delete(input.transactionIdHash);

    if (input.replacedSessionIdHash !== undefined) {
      this.sessions.delete(input.replacedSessionIdHash);
    }

    this.sessions.set(input.session.sessionIdHash, input.session);

    return true;
  }

  async acquireRefreshLease(input: {
    readonly sessionIdHash: string;
    readonly leaseId: string;
    readonly leaseExpiresAt: number;
  }): Promise<'acquired' | 'held' | 'missing'> {
    const session = this.sessions.get(input.sessionIdHash);

    if (session === undefined) {
      return 'missing';
    }

    if (session.refreshLeaseId !== undefined) {
      return 'held';
    }

    this.sessions.set(input.sessionIdHash, {
      ...session,
      refreshLeaseId: input.leaseId,
      refreshLeaseExpiresAt: input.leaseExpiresAt,
    });

    return 'acquired';
  }

  async releaseRefreshLease(
    sessionIdHash: string,
    leaseId: string,
  ): Promise<void> {
    const session = this.sessions.get(sessionIdHash);

    if (session?.refreshLeaseId === leaseId) {
      this.sessions.set(sessionIdHash, {
        ...session,
        refreshLeaseId: undefined,
        refreshLeaseExpiresAt: undefined,
      });
    }
  }

  async updateSessionAfterRefresh(input: {
    readonly sessionIdHash: string;
    readonly leaseId: string;
    readonly subjectId: string;
    readonly roles: readonly string[];
    readonly permissions: readonly string[];
    readonly accessExpiresAt: number;
    readonly refreshToken: IPlatformSecretCiphertext;
  }): Promise<boolean> {
    const session = this.sessions.get(input.sessionIdHash);

    if (session?.refreshLeaseId !== input.leaseId) {
      return false;
    }

    this.sessions.set(input.sessionIdHash, {
      ...session,
      subjectId: input.subjectId,
      roles: input.roles,
      permissions: input.permissions,
      accessExpiresAt: input.accessExpiresAt,
      refreshToken: input.refreshToken,
    });

    return true;
  }
}

function createOidcClient(): IPlatformOidcClientFacade {
  return {
    createAuthorizationUrl: vi.fn(
      async ({ state }) => `https://id.example.test/authorize?state=${state}`,
    ),
    exchangeAuthorizationCode: vi.fn(async () => ({
      accessToken: 'access-token',
      refreshToken: 'refresh-token',
      expiresIn: 300,
    })),
    refresh: vi.fn(async () => ({
      accessToken: 'refreshed-access-token',
      refreshToken: 'rotated-refresh-token',
      expiresIn: 300,
    })),
    revoke: vi.fn(async (): Promise<void> => undefined),
    isInvalidGrant: (): boolean => false,
  };
}

function createRuntime(store = new MemoryStore()) {
  const oidcClient = createOidcClient();
  const accessTokenResolver = {
    resolve: async (request: {
      readonly headers: Readonly<Record<string, readonly string[]>>;
    }): Promise<PlatformRequestIdentityType> =>
      request.headers.authorization?.[0] === 'Bearer rejected'
        ? new PlatformAnonymousIdentity()
        : new PlatformDelegatedIdentity({
            subjectId: 'operator-42',
            roles: ['admin'],
            permissions: ['plugins:read'],
          }),
  };
  return {
    store,
    oidcClient,
    runtime: createPlatformOidcSessionRuntime(
      {
        issuer: 'https://id.example.test',
        jwksUri: 'https://id.example.test/jwks',
        authorizationEndpoint: 'https://id.example.test/authorize',
        tokenEndpoint: 'https://id.example.test/token',
        revocationEndpoint: 'https://id.example.test/revoke',
        redirectUri: 'https://admin.example.test/auth/callback',
        clientId: 'admin-client',
        clientSecret: 'secret',
        scopes: ['openid', 'offline_access'],
        audiences: ['admin-api'],
      },
      {
        store,
        cipher: createCipher(),
        accessTokenResolver,
        oidcClient,
        clock: createClock(),
      },
    ),
  };
}

function createRequest(
  overrides: Partial<IPlatformHttpRequest> = {},
): IPlatformHttpRequest {
  return {
    method: 'GET',
    path: '/auth/login',
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
  route: IPlatformHttpRouteRegistration,
  request: IPlatformHttpRequest,
) {
  return route.execute({
    request,
    baseContext: {
      correlationId: request.correlationId,
      identity: request.identity,
      signal: new AbortController().signal,
    },
  });
}

describe('OIDC session runtime', (): void => {
  it('adapts OIDC routes and resolver to the generic provider facade', (): void => {
    // Arrange
    const { runtime } = createRuntime();

    // Act
    const provider = createPlatformOidcAuthenticationProvider(runtime);

    // Assert
    expect(provider.mode).toBe('oidc');
    expect(provider.resolver).toBe(runtime.resolver);
    expect(provider.publicRouteRegistrations).toBe(runtime.routes);
  });

  it('creates a one-time PKCE transaction and then exchanges it for a strict session cookie', async (): Promise<void> => {
    // Arrange
    const { runtime, oidcClient } = createRuntime();
    const login = runtime.routes.find((route) => route.route === '/auth/login');
    const callback = runtime.routes.find(
      (route) => route.route === '/auth/callback',
    );

    if (login === undefined || callback === undefined) {
      throw new Error('Missing OIDC routes.');
    }

    // Act
    const loginResponse = await execute(login, createRequest());
    const transactionId = loginResponse.cookies?.[0]?.value;

    if (transactionId === undefined) {
      throw new Error('Missing transaction cookie.');
    }
    const state = vi.mocked(oidcClient.createAuthorizationUrl).mock
      .calls[0]?.[0].state;

    if (state === undefined) {
      throw new Error('Missing transaction.');
    }

    const callbackResponse = await execute(
      callback,
      createRequest({
        path: '/auth/callback',
        query: { code: ['code-value'], state: [state] },
        headers: { cookie: [`__Host-prosto-admin-oidc-tx=${transactionId}`] },
      }),
    );

    // Assert
    expect(loginResponse.status).toBe(302);
    expect(oidcClient.createAuthorizationUrl).toHaveBeenCalledOnce();
    expect(callbackResponse.status).toBe(302);
    expect(callbackResponse.headers.location).toBe('/');
    expect(callbackResponse.cookies?.[0]).toMatchObject({
      name: '__Host-prosto-admin-session-v1',
      httpOnly: true,
      secure: true,
      sameSite: 'strict',
    });
    expect(oidcClient.exchangeAuthorizationCode).toHaveBeenCalledOnce();
  });

  it('rejects conflicting session cookies as unauthenticated before reading the store', async (): Promise<void> => {
    // Arrange
    const { runtime, store } = createRuntime();

    // Act and assert
    await expect(
      runtime.resolver.resolve({
        correlationId: 'correlation-42',
        method: 'GET',
        path: '/admin/plugins',
        headers: {
          cookie: [
            '__Host-prosto-admin-session-v1=one; __Host-prosto-admin-session-v1=two',
          ],
        },
        params: {},
        query: {},
      }),
    ).rejects.toMatchObject({ code: 'HTTP_UNAUTHENTICATED' });
    expect(store.sessions.size).toBe(0);
  });

  it('rotates an expiring refresh token and returns the verified delegated identity', async (): Promise<void> => {
    // Arrange
    const { runtime, store, oidcClient } = createRuntime();
    const sessionId = 's'.repeat(43);
    const sessionIdHash = hash(sessionId);
    store.sessions.set(sessionIdHash, {
      sessionIdHash,
      subjectId: 'operator-42',
      roles: ['admin'],
      permissions: ['plugins:read'],
      createdAt: NOW,
      lastSeenAt: NOW,
      absoluteExpiresAt: NOW + 60 * 60 * 1000,
      accessExpiresAt: NOW + 30_000,
      refreshToken: await createCipher().encrypt({
        plaintext: Buffer.from('old-refresh-token'),
        aad: {
          schemaVersion: 1,
          recordHash: sessionIdHash,
          purpose: 'refresh-token',
        },
      }),
    });

    // Act
    const identity = await runtime.resolver.resolve({
      correlationId: 'correlation-42',
      method: 'GET',
      path: '/admin/plugins',
      headers: { cookie: [`__Host-prosto-admin-session-v1=${sessionId}`] },
      params: {},
      query: {},
    });

    // Assert
    expect(identity).toMatchObject({
      authenticationType: 'delegated',
      subjectId: 'operator-42',
    });
    expect(oidcClient.refresh).toHaveBeenCalledWith('old-refresh-token');
    expect(store.sessions.get(sessionIdHash)?.accessExpiresAt).toBe(
      NOW + 300_000,
    );
  });
});
