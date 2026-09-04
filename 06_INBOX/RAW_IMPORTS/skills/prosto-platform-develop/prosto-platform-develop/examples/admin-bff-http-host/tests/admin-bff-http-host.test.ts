import { createServer } from 'node:net';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { IAdminBffLogger } from '@prosto/platform-adapter-admin-bff';
import {
  ADMIN_PERMISSION_POLICY_SCHEMA_VERSION,
  ADMIN_UI_PLUGIN_MANIFEST_SCHEMA_VERSION,
  type IAdminPermissionPolicy,
  type IAdminUIPluginManifest,
} from '@prosto/platform-admin-contracts';
import {
  PlatformDelegatedIdentity,
  PlatformHttpResponse,
  type IPlatformIdentityResolutionRequest,
  type IPlatformHttpRouteRegistration,
} from '@prosto/platform-sdk';
import {
  PLATFORM_LOCAL_AUTH_CSRF_COOKIE_NAME,
  PLATFORM_LOCAL_AUTH_SESSION_COOKIE_NAME,
} from '@prosto/platform-adapter-auth-local';
import {
  PLATFORM_AUTH_LOCAL_SESSION_MODULE_MANIFEST,
  PlatformAuthLocalSessionModule,
} from '@prosto/platform-module-auth-local-session';
import { TypeOrmPersistenceProvider } from '@prosto/platform-adapter-typeorm';
import { PlatformAdminBffRuntimeHost } from '@/index.js';

const permissionPolicy: IAdminPermissionPolicy = {
  schemaVersion: ADMIN_PERMISSION_POLICY_SCHEMA_VERSION,
  roleMappings: [{ roleId: 'admin', permissions: ['catalog.read'] }],
  actionGates: [
    {
      actionId: 'publish',
      requiredPermissions: ['catalog.read'],
      match: 'all',
      effect: 'allow',
    },
  ],
};

const manifest: IAdminUIPluginManifest = {
  schemaVersion: ADMIN_UI_PLUGIN_MANIFEST_SCHEMA_VERSION,
  id: 'catalog-admin-ui',
  version: '1.0.0',
  displayName: 'Catalog Admin UI',
  shellCompatibility: '>=1.0.0',
  requiredPermissions: ['catalog.read'],
  requiredCapabilities: ['catalog'],
  extensionPoints: ['nav'],
  trustClass: 'trusted',
  reviewStatus: 'approved',
  metadata: { owner: 'platform' },
};

async function findAvailablePort(): Promise<number> {
  const listener = createServer();
  await new Promise<void>((resolve, reject): void => {
    listener.once('error', reject);
    listener.listen(0, '127.0.0.1', resolve);
  });
  const address = listener.address();

  if (!address || typeof address === 'string') {
    throw new Error('Could not allocate a TCP port for the test.');
  }

  await new Promise<void>((resolve, reject): void => {
    listener.close((error): void => (error ? reject(error) : resolve()));
  });

  return address.port;
}

function createLogger(): IAdminBffLogger {
  return { debug: vi.fn(), info: vi.fn(), warn: vi.fn(), error: vi.fn() };
}

function cookieValue(response: Response, name: string): string {
  const value = response.headers
    .getSetCookie()
    .find((cookie) => cookie.startsWith(`${name}=`))
    ?.split(';', 1)[0]
    ?.slice(name.length + 1);

  if (value === undefined) {
    throw new Error(`Response did not set ${name} cookie.`);
  }

  return value;
}

function cookieHeader(session: string, csrf: string): string {
  return `${PLATFORM_LOCAL_AUTH_SESSION_COOKIE_NAME}=${session}; ${PLATFORM_LOCAL_AUTH_CSRF_COOKIE_NAME}=${csrf}`;
}

function createIdentityRequest(
  path: string,
  authorization?: readonly string[],
): IPlatformIdentityResolutionRequest {
  return {
    correlationId: 'auth-selection-42',
    method: 'GET',
    path,
    headers: authorization === undefined ? {} : { authorization },
    params: {},
    query: {},
  };
}

describe('RuntimeBuilder Admin BFF HTTP composition root', (): void => {
  const hosts: { stop(): Promise<void> }[] = [];

  afterEach(async (): Promise<void> => {
    await Promise.all(hosts.splice(0).map((host) => host.stop()));
  });

  it('starts RuntimeBuilder before Fastify and serves BFF plus runtime diagnostics', async (): Promise<void> => {
    // Arrange
    const port = await findAvailablePort();
    const fetchUIPluginManifests = vi.fn(async () => [manifest]);
    const resolver = vi.fn(async (request) =>
      request.path === '/admin/api/v1/health' &&
      request.headers['x-delegated-request']?.[0] !== 'true'
        ? {
            authenticationType: 'anonymous' as const,
            roles: [],
            permissions: [],
          }
        : new PlatformDelegatedIdentity({
            subjectId: 'operator-42',
            roles: ['admin'],
            permissions: [],
          }),
    );
    const authenticationStatusRoute: IPlatformHttpRouteRegistration = {
      method: 'GET',
      route: '/admin/api/v1/auth/session',
      execute: async () =>
        new PlatformHttpResponse({
          status: 200,
          body: {
            variant: 'json',
            data: { mode: 'local', state: 'anonymous' },
          },
        }),
    };
    const host = PlatformAdminBffRuntimeHost.create({
      http: {
        host: '127.0.0.1',
        port,
      },
      authenticationProvider: {
        mode: 'local',
        resolver: { resolve: resolver },
        publicRouteRegistrations: [authenticationStatusRoute],
      },
      runtime: { environment: 'test', commandLineArgs: [] },
      adminBff: {
        catalogSource: { fetchUIPluginManifests },
        permissionPolicy,
        shellVersion: '1.0.0',
        environment: 'test',
        discoveryPipelineVersion: 'example.v1',
        logger: createLogger(),
      },
      additionalRouteRegistrations: [
        {
          method: 'GET',
          route: '/auth/test-registration',
          execute: async () =>
            new PlatformHttpResponse({
              status: 204,
            }),
        } satisfies IPlatformHttpRouteRegistration,
      ],
    });
    hosts.push(host);

    // Act
    await host.start();
    const [
      discovery,
      action,
      adminHealth,
      diagnostics,
      health,
      readiness,
      anonymous,
      authenticationStatus,
      additionalRoute,
    ] = await Promise.all([
      fetch(`http://127.0.0.1:${port}/admin/api/v1/discovery`, {
        headers: { 'x-correlation-id': 'discovery-42' },
      }),
      fetch(`http://127.0.0.1:${port}/admin/api/v1/action/publish`, {
        method: 'POST',
      }),
      fetch(`http://127.0.0.1:${port}/admin/api/v1/health`, {
        headers: { 'x-delegated-request': 'true' },
      }),
      fetch(`http://127.0.0.1:${port}/admin/api/v1/diagnostics`),
      fetch(`http://127.0.0.1:${port}/platform/health`),
      fetch(`http://127.0.0.1:${port}/platform/ready`),
      fetch(`http://127.0.0.1:${port}/admin/api/v1/health`),
      fetch(`http://127.0.0.1:${port}/admin/api/v1/auth/session`),
      fetch(`http://127.0.0.1:${port}/auth/test-registration`),
    ]);

    // Assert
    expect(host.runtime.started).toBe(true);
    expect(discovery.status).toBe(200);
    expect(discovery.headers.get('x-correlation-id')).toBe('discovery-42');
    expect((await discovery.json()).data.plugins).toHaveLength(1);
    expect(action.status).toBe(200);
    expect((await action.json()).data.actionId).toBe('publish');
    expect(adminHealth.status).toBe(200);
    expect(diagnostics.status).toBe(200);
    expect(health.status).toBe(200);
    expect(readiness.status).toBe(200);
    expect(anonymous.status).toBe(401);
    expect((await anonymous.json()).error.code).toBe('UNAUTHENTICATED');
    expect(authenticationStatus.status).toBe(200);
    expect(await authenticationStatus.json()).toMatchObject({
      mode: 'local',
      state: 'anonymous',
    });
    expect(additionalRoute.status).toBe(204);
    expect(fetchUIPluginManifests).toHaveBeenCalledTimes(3);

    await host.stop();
    expect(host.runtime.stopped).toBe(true);
  });

  it('bridges anonymous OIDC session status without exposing local mutations', async (): Promise<void> => {
    // Arrange
    const port = await findAvailablePort();
    const host = PlatformAdminBffRuntimeHost.create({
      http: { host: '127.0.0.1', port },
      authenticationProvider: {
        mode: 'oidc',
        resolver: {
          resolve: async () => ({
            authenticationType: 'anonymous' as const,
            roles: [],
            permissions: [],
          }),
        },
        publicRouteRegistrations: [],
      },
      runtime: { environment: 'test', commandLineArgs: [] },
      adminBff: {
        catalogSource: { fetchUIPluginManifests: async () => [] },
        permissionPolicy,
        shellVersion: '1.0.0',
        environment: 'test',
        discoveryPipelineVersion: 'example.v1',
        logger: createLogger(),
      },
    });
    hosts.push(host);

    // Act
    await host.start();
    const session = await fetch(
      `http://127.0.0.1:${port}/admin/api/v1/auth/session`,
    );
    const login = await fetch(
      `http://127.0.0.1:${port}/admin/api/v1/auth/login`,
      { method: 'POST' },
    );

    // Assert
    expect(session.status).toBe(200);
    expect(await session.json()).toEqual({
      mode: 'oidc',
      state: 'anonymous',
      loginUrl: '/auth/login',
      schemaVersion: 'admin-authentication-api.v1',
    });
    expect(login.status).toBe(404);
  });

  it('persists a local authentication lifecycle across restart without exposing the bootstrap password', async (): Promise<void> => {
    // Arrange
    const directory = await mkdtemp(join(tmpdir(), 'prosto-local-auth-e2e-'));
    const port = await findAvailablePort();
    const origin = `http://127.0.0.1:${port}`;
    const bootstrapOutput: string[] = [];
    await writeFile(
      join(directory, 'app_settings.json'),
      JSON.stringify({
        persistence: {
          typeorm: {
            enabled: true,
            type: 'sqlite',
            database: join(directory, 'local-auth.sqlite'),
          },
        },
      }),
    );
    const createHost = (
      module: PlatformAuthLocalSessionModule,
    ): PlatformAdminBffRuntimeHost =>
      PlatformAdminBffRuntimeHost.create({
        http: { host: '127.0.0.1', port },
        authenticationProvider: module.facade.provider,
        runtime: {
          configDir: directory,
          environment: 'test',
          commandLineArgs: [],
          persistenceProvider: new TypeOrmPersistenceProvider(),
          modules: [
            {
              type: 'memory',
              manifest: PLATFORM_AUTH_LOCAL_SESSION_MODULE_MANIFEST,
              module,
            },
          ],
        },
        adminBff: {
          catalogSource: { fetchUIPluginManifests: async () => [] },
          permissionPolicy,
          shellVersion: '1.0.0',
          environment: 'test',
          discoveryPipelineVersion: 'local-auth-e2e.v1',
          logger: createLogger(),
        },
      });

    let firstHost: PlatformAdminBffRuntimeHost | undefined;
    let secondHost: PlatformAdminBffRuntimeHost | undefined;

    try {
      const firstModule = new PlatformAuthLocalSessionModule({
        origin,
        bootstrapOutput: {
          isInteractive: true,
          write: (message: string): void => bootstrapOutput.push(message),
        },
      });
      firstHost = createHost(firstModule);
      hosts.push(firstHost);
      await firstHost.start();
      const password = /One-time password: ([^\n]+)/u.exec(
        bootstrapOutput.join(''),
      )?.[1];

      if (password === undefined) {
        throw new Error('Interactive bootstrap did not provide a password.');
      }

      // Act
      const anonymousDiscovery = await fetch(
        `${origin}/admin/api/v1/discovery`,
      );
      const anonymousSession = await fetch(
        `${origin}/admin/api/v1/auth/session`,
      );
      const loginCsrf = cookieValue(
        anonymousSession,
        PLATFORM_LOCAL_AUTH_CSRF_COOKIE_NAME,
      );
      const login = await fetch(`${origin}/admin/api/v1/auth/login`, {
        method: 'POST',
        headers: {
          'content-type': 'application/json',
          origin,
          cookie: `${PLATFORM_LOCAL_AUTH_CSRF_COOKIE_NAME}=${loginCsrf}`,
          'x-prosto-csrf': loginCsrf,
        },
        body: JSON.stringify({
          schemaVersion: 'admin-authentication-api.v1',
          username: 'admin',
          password,
        }),
      });
      const forcedSession = cookieValue(
        login,
        PLATFORM_LOCAL_AUTH_SESSION_COOKIE_NAME,
      );
      const forcedCsrf = cookieValue(
        login,
        PLATFORM_LOCAL_AUTH_CSRF_COOKIE_NAME,
      );
      const forcedDiscovery = await fetch(`${origin}/admin/api/v1/discovery`, {
        headers: { cookie: cookieHeader(forcedSession, forcedCsrf) },
      });
      const changed = await fetch(
        `${origin}/admin/api/v1/auth/change-password`,
        {
          method: 'POST',
          headers: {
            'content-type': 'application/json',
            origin,
            cookie: cookieHeader(forcedSession, forcedCsrf),
            'x-prosto-csrf': forcedCsrf,
          },
          body: JSON.stringify({
            schemaVersion: 'admin-authentication-api.v1',
            currentPassword: password,
            newPassword: 'replacement-password-42',
          }),
        },
      );
      const authenticatedSession = cookieValue(
        changed,
        PLATFORM_LOCAL_AUTH_SESSION_COOKIE_NAME,
      );
      const authenticatedCsrf = cookieValue(
        changed,
        PLATFORM_LOCAL_AUTH_CSRF_COOKIE_NAME,
      );

      // Assert
      expect(password).toBeDefined();
      expect(bootstrapOutput.join('')).not.toContain('passwordHash');
      expect(anonymousDiscovery.status).toBe(401);
      expect(login.status).toBe(200);
      expect(await login.json()).toMatchObject({
        state: 'password-change-required',
      });
      expect(forcedDiscovery.status).toBe(401);
      expect(changed.status).toBe(200);
      expect(authenticatedSession).not.toBe(forcedSession);
      expect(
        await fetch(`${origin}/admin/api/v1/discovery`, {
          headers: {
            cookie: cookieHeader(authenticatedSession, authenticatedCsrf),
          },
        }),
      ).toHaveProperty('status', 200);

      await firstHost.stop();
      const secondModule = new PlatformAuthLocalSessionModule({
        origin,
        bootstrapOutput: { isInteractive: false, write: (): void => undefined },
      });
      secondHost = createHost(secondModule);
      hosts.push(secondHost);
      await secondHost.start();
      const persistedDiscovery = await fetch(
        `${origin}/admin/api/v1/discovery`,
        {
          headers: {
            cookie: cookieHeader(authenticatedSession, authenticatedCsrf),
          },
        },
      );
      const session = await fetch(`${origin}/admin/api/v1/auth/session`, {
        headers: {
          cookie: cookieHeader(authenticatedSession, authenticatedCsrf),
        },
      });
      const logoutCsrf = cookieValue(
        session,
        PLATFORM_LOCAL_AUTH_CSRF_COOKIE_NAME,
      );
      const logout = await fetch(`${origin}/admin/api/v1/auth/logout`, {
        method: 'POST',
        headers: {
          'content-type': 'application/json',
          origin,
          cookie: cookieHeader(authenticatedSession, logoutCsrf),
          'x-prosto-csrf': logoutCsrf,
        },
        body: JSON.stringify({ schemaVersion: 'admin-authentication-api.v1' }),
      });

      expect(persistedDiscovery.status).toBe(200);
      expect(logout.status).toBe(200);
      expect(
        await fetch(`${origin}/admin/api/v1/discovery`, {
          headers: { cookie: cookieHeader(authenticatedSession, logoutCsrf) },
        }),
      ).toHaveProperty('status', 401);
    } finally {
      await secondHost?.stop();
      await firstHost?.stop();
      await rm(directory, { recursive: true, force: true });
    }
  });
});

describe('OIDC authentication provider facade', (): void => {
  it('keeps bearer authoritative and derives anonymous recovery routes from registrations', async (): Promise<void> => {
    // Arrange
    const bearerIdentity = new PlatformDelegatedIdentity({
      subjectId: 'bearer-operator',
      roles: [],
      permissions: [],
    });
    const sessionIdentity = new PlatformDelegatedIdentity({
      subjectId: 'session-operator',
      roles: [],
      permissions: [],
    });
    const bearerResolver = { resolve: vi.fn(async () => bearerIdentity) };
    const sessionResolver = { resolve: vi.fn(async () => sessionIdentity) };
    const { createPlatformOidcAuthenticationProvider } =
      await import('@prosto/platform-adapter-auth-oidc-session');
    const resolver = createPlatformOidcAuthenticationProvider(
      {
        resolver: sessionResolver,
        routes: [
          {
            method: 'GET',
            route: '/auth/login',
            execute: vi.fn(),
          },
          {
            method: 'POST',
            route: '/auth/logout',
            execute: vi.fn(),
          },
        ],
      },
      bearerResolver,
    ).resolver;

    // Act
    const [recoveryIdentity, sessionRouteIdentity, bearerRouteIdentity] =
      await Promise.all([
        resolver.resolve(createIdentityRequest('/auth/login')),
        resolver.resolve(createIdentityRequest('/admin/api/v1/discovery')),
        resolver.resolve(
          createIdentityRequest('/auth/logout', ['Bearer supplied-token']),
        ),
      ]);

    // Assert
    expect(recoveryIdentity.authenticationType).toBe('anonymous');
    expect(sessionRouteIdentity).toBe(sessionIdentity);
    expect(bearerRouteIdentity).toBe(bearerIdentity);
    expect(sessionResolver.resolve).toHaveBeenCalledTimes(1);
    expect(bearerResolver.resolve).toHaveBeenCalledTimes(1);
  });
});
