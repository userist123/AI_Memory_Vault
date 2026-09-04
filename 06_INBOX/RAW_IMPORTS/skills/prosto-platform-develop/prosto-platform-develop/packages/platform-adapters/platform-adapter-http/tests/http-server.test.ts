import { describe, expect, it, vi } from 'vitest';
import {
  ConsoleHttpLogger,
  PlatformHttpServer,
  PlatformHttpServerLifecycleError,
  type IPlatformHttpLogger,
} from '@/index.js';
import type { FastifyInstance } from 'fastify';
import {
  PlatformDelegatedIdentity,
  PlatformHttpError,
  type IPlatformHttpRouteRegistration,
  type IPlatformHttpRouteContextFactoryInput,
} from '@prosto/platform-sdk';

const registrations: IPlatformHttpRouteRegistration[] = [
  {
    method: 'GET',
    route: '/health',
    execute: vi.fn(),
  },
];

function createLogger(): IPlatformHttpLogger {
  return {
    debug: vi.fn(),
    info: vi.fn(),
    warn: vi.fn(),
    error: vi.fn(),
  };
}

function captureError(action: () => void): Error {
  try {
    action();
  } catch (error: unknown) {
    if (error instanceof Error) {
      return error;
    }
  }

  throw new Error('Expected action to throw an Error.');
}

function getFastifyForTest(server: PlatformHttpServer): FastifyInstance {
  return (server as unknown as { readonly _fastify: FastifyInstance })._fastify;
}

describe('PlatformHttpServer', (): void => {
  it('transitions through route registration and rejects repeated start', async (): Promise<void> => {
    // Arrange
    const server = new PlatformHttpServer({
      host: '127.0.0.1',
      port: 0,
      logger: createLogger(),
    });

    server.registerRoutes(registrations);

    // Act
    await server.start();

    // Assert
    expect(server.state).toBe('started');
    await expect(server.start()).rejects.toMatchObject({
      code: 'INVALID_LIFECYCLE_TRANSITION',
      state: 'started',
    });

    await server.stop();
  });

  it('returns the same promise for concurrent shutdown calls', async (): Promise<void> => {
    // Arrange
    const server = new PlatformHttpServer({
      host: '127.0.0.1',
      port: 0,
      logger: createLogger(),
    });

    server.registerRoutes(registrations);
    await server.start();

    // Act
    const firstStop = server.stop();
    const secondStop = server.stop();

    await Promise.all([firstStop, secondStop]);

    // Assert
    expect(secondStop).toBe(firstStop);
    expect(server.state).toBe('stopped');
  });

  it('binds an ephemeral TCP listener and releases it on shutdown', async (): Promise<void> => {
    // Arrange
    const server = new PlatformHttpServer({
      host: '127.0.0.1',
      port: 0,
      logger: createLogger(),
    });
    server.registerRoutes([
      {
        method: 'GET',
        route: '/tcp-health',
        execute: async () => ({
          status: 200,
          headers: {},
          body: { variant: 'json' as const, data: { status: 'ok' } },
        }),
      },
    ]);

    // Act
    await server.start();
    const address = getFastifyForTest(server).server.address();

    // Assert
    expect(address).not.toBeNull();
    expect(typeof address).not.toBe('string');

    if (address === null || typeof address === 'string') {
      throw new Error('Expected the HTTP server to use a TCP socket.');
    }

    const response = await fetch(`http://127.0.0.1:${address.port}/tcp-health`);
    expect(response.status).toBe(200);
    await expect(response.json()).resolves.toEqual({ status: 'ok' });

    await server.stop();
    expect(getFastifyForTest(server).server.listening).toBe(false);
  });

  it('rejects invalid lifecycle transitions and invalid configuration', (): void => {
    // Arrange
    const server = new PlatformHttpServer({ host: '127.0.0.1', port: 0 });

    // Act and assert
    expect((): void => server.registerRoutes([])).toThrow(
      PlatformHttpServerLifecycleError,
    );

    expect(
      captureError(() => new PlatformHttpServer({ host: '', port: 0 })),
    ).toMatchObject({ code: 'INVALID_SERVER_CONFIGURATION' });

    expect(
      captureError(
        () =>
          new PlatformHttpServer({
            host: '127.0.0.1',
            port: 0,
            requestTimeoutMs: 0,
          }),
      ),
    ).toMatchObject({ code: 'INVALID_SERVER_CONFIGURATION' });
  });

  it('dispatches an SDK route with normalized metadata and resolved identity', async (): Promise<void> => {
    // Arrange
    let capturedInput: IPlatformHttpRouteContextFactoryInput | undefined;

    const resolve = vi.fn(
      async () =>
        new PlatformDelegatedIdentity({
          subjectId: 'operator-42',
          roles: ['admin'],
          permissions: ['plugins:read'],
        }),
    );

    const server = new PlatformHttpServer({
      host: '127.0.0.1',
      port: 0,
      identityResolver: { resolve },
      logger: createLogger(),
    });

    const registration: IPlatformHttpRouteRegistration = {
      method: 'GET',
      route: '/items/:itemId',
      execute: async (input) => {
        capturedInput = input;

        return {
          status: 200,
          headers: {},
          body: { variant: 'json', data: { ok: true } },
        };
      },
    };

    server.registerRoutes([registration]);

    // Act
    const response = await getFastifyForTest(server).inject({
      method: 'GET',
      url: '/items/alpha?tag=first&tag=second',
      headers: {
        'x-correlation-id': 'request-42',
        'x-repeat': ['first', 'second'],
      },
    });

    // Assert
    expect(response.statusCode).toBe(200);
    expect(response.json()).toEqual({ ok: true });
    expect(response.headers['x-correlation-id']).toBe('request-42');
    expect(resolve).toHaveBeenCalledTimes(1);
    expect(capturedInput?.request.params).toEqual({ itemId: 'alpha' });
    expect(capturedInput?.request.query).toEqual({ tag: ['first', 'second'] });
    expect(capturedInput?.request.headers['x-repeat']).toEqual([
      'first,second',
    ]);
    expect(capturedInput?.baseContext.correlationId).toBe('request-42');
    expect(capturedInput?.baseContext.identity).toMatchObject({
      authenticationType: 'delegated',
      subjectId: 'operator-42',
    });
  });

  it('rejects duplicate normalized route shapes without mutating the registry', (): void => {
    // Arrange
    const server = new PlatformHttpServer({ host: '127.0.0.1', port: 0 });
    const first: IPlatformHttpRouteRegistration = {
      method: 'GET',
      route: '/items/:itemId',
      execute: vi.fn(),
    };
    const duplicate: IPlatformHttpRouteRegistration = {
      method: 'GET',
      route: '/items/:slug',
      execute: vi.fn(),
    };

    server.registerRoutes([first]);

    // Act and assert
    expect((): void => server.registerRoutes([duplicate])).toThrow(
      PlatformHttpError,
    );
    expect(
      captureError(() => server.registerRoutes([duplicate])),
    ).toMatchObject({
      code: 'DUPLICATE_ROUTE',
    });
    expect(server.state).toBe('routesRegistered');
  });

  it('rejects an invalid multi-route batch atomically without registering its valid prefix', async (): Promise<void> => {
    // Arrange
    const server = new PlatformHttpServer({
      host: '127.0.0.1',
      port: 0,
      logger: createLogger(),
    });
    const existingExecute = vi.fn(async () => ({
      status: 200,
      headers: {},
      body: { variant: 'json' as const, data: { route: 'existing' } },
    }));
    const acceptedPrefixExecute = vi.fn();

    server.registerRoutes([
      { method: 'GET', route: '/existing', execute: existingExecute },
    ]);

    // Act and assert
    expect(
      captureError(() =>
        server.registerRoutes([
          {
            method: 'GET',
            route: '/would-be-registered',
            execute: acceptedPrefixExecute,
          },
          {
            method: 'GET',
            route: '/invalid/',
            execute: vi.fn(),
          },
        ]),
      ),
    ).toMatchObject({ code: 'INVALID_ROUTE_GRAMMAR' });

    // Assert
    const fastify = getFastifyForTest(server);
    const existingResponse = await fastify.inject({
      method: 'GET',
      url: '/existing',
    });
    const rejectedPrefixResponse = await fastify.inject({
      method: 'GET',
      url: '/would-be-registered',
      headers: { 'x-correlation-id': '11111111-1111-4111-8111-111111111111' },
    });

    expect(existingResponse.statusCode).toBe(200);
    expect(existingExecute).toHaveBeenCalledTimes(1);
    expect(rejectedPrefixResponse.statusCode).toBe(404);
    expect(rejectedPrefixResponse.json().error.code).toBe('ROUTE_NOT_FOUND');
    expect(acceptedPrefixExecute).not.toHaveBeenCalled();
    expect(server.state).toBe('routesRegistered');
  });

  it.each([
    ['root path', '/'],
    ['missing leading slash', 'items'],
    ['trailing slash', '/items/'],
    ['empty path segment', '/items//active'],
    ['invalid parameter identifier', '/items/:item-id'],
    ['forbidden literal character', '/items/{itemId}'],
  ])('rejects invalid route grammar for %s', (_scenario, route): void => {
    // Arrange
    const server = new PlatformHttpServer({ host: '127.0.0.1', port: 0 });

    // Act and assert
    expect(
      captureError(() =>
        server.registerRoutes([{ method: 'GET', route, execute: vi.fn() }]),
      ),
    ).toMatchObject({ code: 'INVALID_ROUTE_GRAMMAR' });

    // Assert
    expect(server.state).toBe('created');
  });

  it('returns a correlated ROUTE_NOT_FOUND envelope for an unsupported method on an existing path', async (): Promise<void> => {
    // Arrange
    const execute = vi.fn();
    const server = new PlatformHttpServer({
      host: '127.0.0.1',
      port: 0,
      logger: createLogger(),
    });
    server.registerRoutes([{ method: 'GET', route: '/reports', execute }]);

    // Act
    const response = await getFastifyForTest(server).inject({
      method: 'POST',
      url: '/reports',
      headers: { 'x-correlation-id': '22222222-2222-4222-8222-222222222222' },
    });

    // Assert
    expect(response.statusCode).toBe(404);
    expect(response.headers['x-correlation-id']).toBe(
      '22222222-2222-4222-8222-222222222222',
    );
    expect(response.json()).toEqual({
      correlationId: '22222222-2222-4222-8222-222222222222',
      error: {
        code: 'ROUTE_NOT_FOUND',
        message: 'The requested route was not found.',
      },
    });
    expect(execute).not.toHaveBeenCalled();
  });

  it.each([
    {
      scenario: 'malformed JSON',
      config: {},
      headers: { 'content-type': 'application/json' },
      payload: '{"missing":',
      expectedStatus: 400,
      expectedCode: 'INVALID_REQUEST_BODY',
    },
    {
      scenario: 'oversized payload',
      config: { bodyLimitBytes: 4 },
      headers: { 'content-type': 'text/plain' },
      payload: 'oversized',
      expectedStatus: 413,
      expectedCode: 'PAYLOAD_TOO_LARGE',
    },
    {
      scenario: 'unsupported media type',
      config: {},
      headers: { 'content-type': 'application/xml' },
      payload: '<report />',
      expectedStatus: 415,
      expectedCode: 'UNSUPPORTED_MEDIA_TYPE',
    },
  ])(
    'returns a correlated $expectedStatus for $scenario request bodies',
    async ({
      config,
      headers,
      payload,
      expectedStatus,
      expectedCode,
    }): Promise<void> => {
      // Arrange
      const execute = vi.fn();
      const correlationId = '33333333-3333-4333-8333-333333333333';
      const server = new PlatformHttpServer({
        host: '127.0.0.1',
        port: 0,
        logger: createLogger(),
        ...config,
      });
      server.registerRoutes([{ method: 'POST', route: '/reports', execute }]);

      // Act
      const response = await getFastifyForTest(server).inject({
        method: 'POST',
        url: '/reports',
        headers: { ...headers, 'x-correlation-id': correlationId },
        payload,
      });

      // Assert
      expect(response.statusCode).toBe(expectedStatus);
      expect(response.headers['x-correlation-id']).toBe(correlationId);
      expect(response.json()).toEqual({
        correlationId,
        error: {
          code: expectedCode,
          message:
            expectedStatus === 400
              ? 'The HTTP request body is invalid.'
              : expectedStatus === 413
                ? 'The HTTP request payload is too large.'
                : 'The request media type is not supported.',
        },
      });
      expect(execute).not.toHaveBeenCalled();
    },
  );

  it('uses a safe correlated 500 envelope when a route throws a secret-bearing error', async (): Promise<void> => {
    // Arrange
    const server = new PlatformHttpServer({
      host: '127.0.0.1',
      port: 0,
      logger: createLogger(),
    });
    server.registerRoutes([
      {
        method: 'GET',
        route: '/failure',
        execute: async (): Promise<never> => {
          throw new Error('database password=do-not-disclose');
        },
      },
    ]);

    // Act
    const response = await getFastifyForTest(server).inject({
      method: 'GET',
      url: '/failure',
      headers: { 'x-correlation-id': '44444444-4444-4444-8444-444444444444' },
    });

    // Assert
    expect(response.statusCode).toBe(500);
    expect(response.headers['x-correlation-id']).toBe(
      '44444444-4444-4444-8444-444444444444',
    );
    expect(response.json()).toEqual({
      correlationId: '44444444-4444-4444-8444-444444444444',
      error: {
        code: 'INTERNAL_ERROR',
        message: 'An unexpected error occurred.',
      },
    });
    expect(response.body).not.toContain('database password');
  });

  it('uses a safe correlated 500 envelope for invalid response metadata', async (): Promise<void> => {
    // Arrange
    const server = new PlatformHttpServer({
      host: '127.0.0.1',
      port: 0,
      logger: createLogger(),
    });
    server.registerRoutes([
      {
        method: 'GET',
        route: '/invalid-response',
        execute: async () =>
          ({
            status: 200,
            body: { variant: 'binary', data: new Uint8Array([1]) },
          }) as never,
      },
    ]);

    // Act
    const response = await getFastifyForTest(server).inject({
      method: 'GET',
      url: '/invalid-response',
      headers: { 'x-correlation-id': '55555555-5555-4555-8555-555555555555' },
    });

    // Assert
    expect(response.statusCode).toBe(500);
    expect(response.json()).toEqual({
      correlationId: '55555555-5555-4555-8555-555555555555',
      error: {
        code: 'INTERNAL_ERROR',
        message: 'An unexpected error occurred.',
      },
    });
  });

  it('rejects handler-controlled CORS response headers with a safe correlated 500 envelope', async (): Promise<void> => {
    // Arrange
    const server = new PlatformHttpServer({
      host: '127.0.0.1',
      port: 0,
      logger: createLogger(),
    });
    server.registerRoutes([
      {
        method: 'GET',
        route: '/invalid-cors-header',
        execute: async () => ({
          status: 200,
          headers: { 'Access-Control-Allow-Origin': 'https://attacker.test' },
          body: { variant: 'json' as const, data: { unsafe: true } },
        }),
      },
    ]);

    // Act
    const response = await getFastifyForTest(server).inject({
      method: 'GET',
      url: '/invalid-cors-header',
      headers: { 'x-correlation-id': '66666666-6666-4666-8666-666666666666' },
    });

    // Assert
    expect(response.statusCode).toBe(500);
    expect(response.json()).toEqual({
      correlationId: '66666666-6666-4666-8666-666666666666',
      error: {
        code: 'INTERNAL_ERROR',
        message: 'An unexpected error occurred.',
      },
    });
    expect(response.headers['access-control-allow-origin']).toBeUndefined();
  });

  it('sends JSON and binary SDK responses with their declared representations', async (): Promise<void> => {
    // Arrange
    const server = new PlatformHttpServer({ host: '127.0.0.1', port: 0 });
    server.registerRoutes([
      {
        method: 'GET',
        route: '/report',
        execute: async () => ({
          status: 200,
          headers: {},
          body: { variant: 'json' as const, data: { reportId: 'rpt-42' } },
        }),
      },
      {
        method: 'GET',
        route: '/report-export',
        execute: async () => ({
          status: 200,
          headers: {},
          body: {
            variant: 'binary' as const,
            data: new Uint8Array([0, 255, 42]),
            contentType: 'application/vnd.prosto.report',
          },
        }),
      },
    ]);

    // Act
    const fastify = getFastifyForTest(server);
    const jsonResponse = await fastify.inject({
      method: 'GET',
      url: '/report',
    });
    const binaryResponse = await fastify.inject({
      method: 'GET',
      url: '/report-export',
    });

    // Assert
    expect(jsonResponse.statusCode).toBe(200);
    expect(jsonResponse.headers['content-type']).toContain('application/json');
    expect(jsonResponse.json()).toEqual({ reportId: 'rpt-42' });
    expect(binaryResponse.statusCode).toBe(200);
    expect(binaryResponse.headers['content-type']).toContain(
      'application/vnd.prosto.report',
    );
    expect(binaryResponse.headers['content-length']).toBe('3');
    expect(binaryResponse.rawPayload).toEqual(Buffer.from([0, 255, 42]));
  });

  it('replaces an unsafe correlation ID, returns it, and emits an audit warning', async (): Promise<void> => {
    // Arrange
    const logger = createLogger();
    let handlerCorrelationId = '';
    const server = new PlatformHttpServer({
      host: '127.0.0.1',
      port: 0,
      logger,
    });
    server.registerRoutes([
      {
        method: 'GET',
        route: '/correlation',
        execute: async (input) => {
          handlerCorrelationId = input.baseContext.correlationId;

          return {
            status: 200,
            headers: {},
            body: { variant: 'json' as const, data: { ok: true } },
          };
        },
      },
    ]);

    // Act
    const response = await getFastifyForTest(server).inject({
      method: 'GET',
      url: '/correlation',
      headers: { 'x-correlation-id': 'unsafe correlation id' },
    });

    // Assert
    const correlationId = response.headers['x-correlation-id'];
    expect(correlationId).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu,
    );
    expect(handlerCorrelationId).toBe(correlationId);
    expect(logger.warn).toHaveBeenCalledWith(
      'Invalid correlation ID was replaced.',
      {
        correlationId,
        errorCode: 'INVALID_CORRELATION_ID',
      },
    );
  });

  it('applies the configured CORS allowlist to simple and preflight requests', async (): Promise<void> => {
    // Arrange
    const origin = 'https://admin.prosto.test';
    const server = new PlatformHttpServer({
      host: '127.0.0.1',
      port: 0,
      cors: {
        allowedOrigins: [origin],
        allowedMethods: ['GET'],
        credentials: true,
      },
    });
    server.registerRoutes([
      {
        method: 'GET',
        route: '/cors-enabled',
        execute: async () => ({
          status: 200,
          headers: {},
          body: { variant: 'json' as const, data: { ok: true } },
        }),
      },
    ]);

    // Act
    const fastify = getFastifyForTest(server);
    const simpleResponse = await fastify.inject({
      method: 'GET',
      url: '/cors-enabled',
      headers: { origin },
    });
    const preflightResponse = await fastify.inject({
      method: 'OPTIONS',
      url: '/cors-enabled',
      headers: {
        origin,
        'access-control-request-method': 'GET',
      },
    });

    // Assert
    expect(simpleResponse.statusCode).toBe(200);
    expect(simpleResponse.headers['access-control-allow-origin']).toBe(origin);
    expect(simpleResponse.headers['access-control-allow-credentials']).toBe(
      'true',
    );
    expect(preflightResponse.statusCode).toBe(204);
    expect(preflightResponse.headers['access-control-allow-origin']).toBe(
      origin,
    );
    expect(preflightResponse.headers['access-control-allow-methods']).toContain(
      'GET',
    );
  });

  it('returns a correlated 503 when identity resolution fails before dispatch', async (): Promise<void> => {
    // Arrange
    const execute = vi.fn();
    const server = new PlatformHttpServer({
      host: '127.0.0.1',
      port: 0,
      identityResolver: {
        resolve: async (): Promise<never> => {
          throw new Error('Identity provider unavailable');
        },
      },
      logger: createLogger(),
    });

    server.registerRoutes([
      {
        method: 'GET',
        route: '/secure',
        execute,
      },
    ]);

    // Act
    const response = await getFastifyForTest(server).inject({
      method: 'GET',
      url: '/secure',
      headers: { 'x-correlation-id': 'request-503' },
    });

    // Assert
    expect(response.statusCode).toBe(503);
    expect(response.headers['x-correlation-id']).toBe('request-503');
    expect(response.json()).toEqual({
      correlationId: 'request-503',
      error: {
        code: 'IDENTITY_RESOLUTION_UNAVAILABLE',
        message: 'Request identity resolution is temporarily unavailable.',
      },
    });
    expect(execute).not.toHaveBeenCalled();
  });

  it('preserves resolver authentication failures and emits a bare Bearer challenge', async (): Promise<void> => {
    // Arrange
    const execute = vi.fn();
    const server = new PlatformHttpServer({
      host: '127.0.0.1',
      port: 0,
      identityResolver: {
        resolve: async (): Promise<never> => {
          throw new PlatformHttpError(
            'HTTP_UNAUTHENTICATED',
            'Bearer credential was rejected.',
          );
        },
      },
      logger: createLogger(),
    });

    server.registerRoutes([{ method: 'GET', route: '/secure', execute }]);

    // Act
    const response = await getFastifyForTest(server).inject({
      method: 'GET',
      url: '/secure',
    });

    // Assert
    expect(response.statusCode).toBe(401);
    expect(response.headers['www-authenticate']).toBe('Bearer');
    expect(response.json()).toEqual({
      correlationId: expect.any(String),
      error: {
        code: 'UNAUTHENTICATED',
        message: 'Authentication is required for this route.',
      },
    });
    expect(response.body).not.toContain('error_description');
    expect(execute).not.toHaveBeenCalled();
  });

  it('preserves a typed resolver availability failure as 503', async (): Promise<void> => {
    // Arrange
    const server = new PlatformHttpServer({
      host: '127.0.0.1',
      port: 0,
      identityResolver: {
        resolve: async (): Promise<never> => {
          throw new PlatformHttpError(
            'IDENTITY_RESOLUTION_UNAVAILABLE',
            'Identity provider outage.',
          );
        },
      },
      logger: createLogger(),
    });

    server.registerRoutes([
      { method: 'GET', route: '/secure', execute: vi.fn() },
    ]);

    // Act
    const response = await getFastifyForTest(server).inject({
      method: 'GET',
      url: '/secure',
    });

    // Assert
    expect(response.statusCode).toBe(503);
    expect(response.json().error.code).toBe('IDENTITY_RESOLUTION_UNAVAILABLE');
  });

  it('maps an invalid resolver identity to a safe correlated 503', async (): Promise<void> => {
    // Arrange
    const execute = vi.fn();
    const server = new PlatformHttpServer({
      host: '127.0.0.1',
      port: 0,
      identityResolver: {
        resolve: async () => ({
          authenticationType: 'delegated',
          subjectId: 'operator-42',
          roles: 'admin' as unknown as string[],
          permissions: [],
        }),
      },
      logger: createLogger(),
    });

    server.registerRoutes([{ method: 'GET', route: '/secure', execute }]);

    // Act
    const response = await getFastifyForTest(server).inject({
      method: 'GET',
      url: '/secure',
    });

    // Assert
    expect(response.statusCode).toBe(503);
    expect(response.json().error.code).toBe('IDENTITY_RESOLUTION_UNAVAILABLE');
    expect(execute).not.toHaveBeenCalled();
  });

  it('uses the anonymous identity when no resolver is configured', async (): Promise<void> => {
    // Arrange
    let authenticationType = '';
    const server = new PlatformHttpServer({ host: '127.0.0.1', port: 0 });
    server.registerRoutes([
      {
        method: 'GET',
        route: '/public',
        execute: async (input) => {
          authenticationType = input.baseContext.identity.authenticationType;
          return { status: 204, headers: {}, body: { variant: 'empty' } };
        },
      },
    ]);

    // Act
    const response = await getFastifyForTest(server).inject({
      method: 'GET',
      url: '/public',
    });

    // Assert
    expect(response.statusCode).toBe(204);
    expect(authenticationType).toBe('anonymous');
  });

  it('maps multiple structured cookies and rejects raw header injection', async (): Promise<void> => {
    // Arrange
    const server = new PlatformHttpServer({ host: '127.0.0.1', port: 0 });
    server.registerRoutes([
      {
        method: 'GET',
        route: '/redirect',
        execute: async () => ({
          status: 302,
          headers: { Location: '/next' },
          body: { variant: 'empty' },
          cookies: [
            {
              name: '__Host-session',
              value: 'opaque',
              path: '/',
              httpOnly: true,
              secure: true,
              sameSite: 'strict' as const,
            },
            {
              name: '__Host-tx',
              value: '',
              path: '/',
              httpOnly: true,
              secure: true,
              sameSite: 'lax' as const,
              maxAge: 0,
            },
          ],
        }),
      },
      {
        method: 'GET',
        route: '/raw-cookie',
        execute: async () =>
          ({
            status: 200,
            headers: { 'Set-Cookie': 'session=unsafe' },
          }) as never,
      },
    ]);

    // Act
    const fastify = getFastifyForTest(server);
    const redirect = await fastify.inject({ method: 'GET', url: '/redirect' });
    const injected = await fastify.inject({
      method: 'GET',
      url: '/raw-cookie',
    });
    const cookies = redirect.headers['set-cookie'];

    // Assert
    expect(redirect.statusCode).toBe(302);
    expect(redirect.headers.location).toBe('/next');
    expect(Array.isArray(cookies) ? cookies : [cookies]).toEqual([
      '__Host-session=opaque; Path=/; HttpOnly; Secure; SameSite=Strict',
      '__Host-tx=; Max-Age=0; Path=/; HttpOnly; Secure; SameSite=Lax',
    ]);
    expect(injected.statusCode).toBe(500);
    expect(injected.headers['set-cookie']).toBeUndefined();
  });

  it('aborts overdue route work and returns a correlated gateway timeout', async (): Promise<void> => {
    // Arrange
    let wasAborted = false;
    const server = new PlatformHttpServer({
      host: '127.0.0.1',
      port: 0,
      requestTimeoutMs: 10,
      logger: createLogger(),
    });

    server.registerRoutes([
      {
        method: 'GET',
        route: '/slow',
        execute: async (input): Promise<never> =>
          new Promise<never>((_resolve): void => {
            input.baseContext.signal.addEventListener('abort', (): void => {
              wasAborted = input.baseContext.signal.aborted;
            });
          }),
      },
    ]);

    // Act
    const response = await getFastifyForTest(server).inject({
      method: 'GET',
      url: '/slow',
      headers: { 'x-correlation-id': 'request-timeout' },
    });

    // Assert
    expect(wasAborted).toBe(true);
    expect(response.statusCode).toBe(504);
    expect(response.headers['x-correlation-id']).toBe('request-timeout');
    expect(response.json()).toEqual({
      correlationId: 'request-timeout',
      error: {
        code: 'GATEWAY_TIMEOUT',
        message: 'The request timed out.',
      },
    });
  });

  it('uses the safe correlated envelope for unknown routes', async (): Promise<void> => {
    // Arrange
    const server = new PlatformHttpServer({
      host: '127.0.0.1',
      port: 0,
      logger: createLogger(),
    });
    server.registerRoutes(registrations);

    // Act
    const response = await getFastifyForTest(server).inject({
      method: 'GET',
      url: '/unknown',
      headers: { 'x-correlation-id': 'request-404' },
    });

    // Assert
    expect(response.statusCode).toBe(404);
    expect(response.headers['x-correlation-id']).toBe('request-404');
    expect(response.json()).toEqual({
      correlationId: 'request-404',
      error: {
        code: 'ROUTE_NOT_FOUND',
        message: 'The requested route was not found.',
      },
    });
  });

  it('suppresses an explicit HEAD stream body and cancels the source stream', async (): Promise<void> => {
    // Arrange
    let wasCancelled = false;
    const source = new ReadableStream<Uint8Array>({
      cancel: (): void => {
        wasCancelled = true;
      },
    });
    const server = new PlatformHttpServer({ host: '127.0.0.1', port: 0 });

    server.registerRoutes([
      {
        method: 'HEAD',
        route: '/download',
        execute: async () => ({
          status: 200,
          headers: {},
          body: {
            variant: 'stream' as const,
            stream: source,
            contentType: 'application/octet-stream',
            contentLength: 4,
          },
        }),
      },
    ]);

    // Act
    const response = await getFastifyForTest(server).inject({
      method: 'HEAD',
      url: '/download',
    });

    // Assert
    expect(response.statusCode).toBe(200);
    expect(response.body).toBe('');
    expect(response.headers['content-type']).toContain(
      'application/octet-stream',
    );
    expect(response.headers['content-length']).toBe('4');
    expect(wasCancelled).toBe(true);
  });

  it('maps all supported finite request body variants without exposing Fastify objects', async (): Promise<void> => {
    // Arrange
    const receivedBodies: unknown[] = [];
    const server = new PlatformHttpServer({ host: '127.0.0.1', port: 0 });
    server.registerRoutes([
      {
        method: 'POST',
        route: '/json',
        execute: async (input) => {
          receivedBodies.push(input.request.body);
          return {
            status: 204,
            headers: {},
            body: { variant: 'empty' as const },
          };
        },
      },
      {
        method: 'POST',
        route: '/text',
        execute: async (input) => {
          receivedBodies.push(input.request.body);
          return {
            status: 204,
            headers: {},
            body: { variant: 'empty' as const },
          };
        },
      },
      {
        method: 'POST',
        route: '/binary',
        execute: async (input) => {
          receivedBodies.push(input.request.body);
          return {
            status: 204,
            headers: {},
            body: { variant: 'empty' as const },
          };
        },
      },
      {
        method: 'POST',
        route: '/empty',
        execute: async (input) => {
          receivedBodies.push(input.request.body);
          return {
            status: 204,
            headers: {},
            body: { variant: 'empty' as const },
          };
        },
      },
    ]);

    // Act
    const fastify = getFastifyForTest(server);
    await fastify.inject({
      method: 'POST',
      url: '/json',
      headers: { 'content-type': 'application/json' },
      payload: JSON.stringify({ enabled: true }),
    });
    await fastify.inject({
      method: 'POST',
      url: '/text',
      headers: { 'content-type': 'text/plain; charset=utf-8' },
      payload: 'Просто',
    });
    await fastify.inject({
      method: 'POST',
      url: '/binary',
      headers: { 'content-type': 'application/octet-stream' },
      payload: Buffer.from([0, 255, 42]),
    });
    await fastify.inject({ method: 'POST', url: '/empty' });

    // Assert
    expect(receivedBodies).toEqual([
      { variant: 'json', data: { enabled: true } },
      { variant: 'text', data: 'Просто' },
      {
        variant: 'binary',
        data: new Uint8Array([0, 255, 42]),
        contentType: 'application/octet-stream',
      },
      { variant: 'empty' },
    ]);
  });

  it('does not synthesize HEAD routes and returns 404 for OPTIONS without CORS', async (): Promise<void> => {
    // Arrange
    const server = new PlatformHttpServer({ host: '127.0.0.1', port: 0 });
    server.registerRoutes([
      {
        method: 'GET',
        route: '/only-get',
        execute: async () => ({
          status: 200,
          headers: {},
          body: { variant: 'json' as const, data: { ok: true } },
        }),
      },
    ]);

    // Act
    const fastify = getFastifyForTest(server);
    const headResponse = await fastify.inject({
      method: 'HEAD',
      url: '/only-get',
    });
    const optionsResponse = await fastify.inject({
      method: 'OPTIONS',
      url: '/only-get',
    });

    // Assert
    expect(headResponse.statusCode).toBe(404);
    expect(optionsResponse.statusCode).toBe(404);
    expect(optionsResponse.json().error.code).toBe('ROUTE_NOT_FOUND');
  });

  it('emits no CORS headers by default or for a disallowed origin', async (): Promise<void> => {
    // Arrange
    const createServer = (cors?: {
      readonly allowedOrigins: readonly string[];
      readonly allowedMethods: readonly 'GET'[];
    }): PlatformHttpServer => {
      const server = new PlatformHttpServer({
        host: '127.0.0.1',
        port: 0,
        ...(cors !== undefined && { cors }),
      });
      server.registerRoutes([
        {
          method: 'GET',
          route: '/resource',
          execute: async () => ({
            status: 200,
            headers: {},
            body: { variant: 'json' as const, data: { ok: true } },
          }),
        },
      ]);
      return server;
    };
    const withoutCors = createServer();
    const withCors = createServer({
      allowedOrigins: ['https://admin.prosto.test'],
      allowedMethods: ['GET'],
    });

    // Act
    const defaultResponse = await getFastifyForTest(withoutCors).inject({
      method: 'GET',
      url: '/resource',
      headers: { origin: 'https://admin.prosto.test' },
    });
    const disallowedResponse = await getFastifyForTest(withCors).inject({
      method: 'GET',
      url: '/resource',
      headers: { origin: 'https://untrusted.prosto.test' },
    });

    // Assert
    expect(
      defaultResponse.headers['access-control-allow-origin'],
    ).toBeUndefined();
    expect(disallowedResponse.statusCode).toBe(200);
    expect(
      disallowedResponse.headers['access-control-allow-origin'],
    ).toBeUndefined();
  });

  it('adds Helmet protection headers before dispatching application routes', async (): Promise<void> => {
    // Arrange
    const server = new PlatformHttpServer({ host: '127.0.0.1', port: 0 });
    server.registerRoutes([
      {
        method: 'GET',
        route: '/protected',
        execute: async () => ({
          status: 204,
          headers: {},
          body: { variant: 'empty' as const },
        }),
      },
    ]);

    // Act
    const response = await getFastifyForTest(server).inject({
      method: 'GET',
      url: '/protected',
    });

    // Assert
    expect(response.headers['x-content-type-options']).toBe('nosniff');
    expect(response.headers['x-frame-options']).toBe('SAMEORIGIN');
  });

  it('redacts sensitive fields recursively in the default console logger', (): void => {
    // Arrange
    const logger = new ConsoleHttpLogger();
    const errorSpy = vi
      .spyOn(console, 'error')
      .mockImplementation((): void => undefined);

    // Act
    logger.error('request failed', {
      authorization: 'Bearer private-token',
      metadata: {
        password: 'private-password',
        nonSensitive: 'retained',
        nested: [{ credential: 'private-credential' }],
      },
    });

    // Assert
    expect(errorSpy).toHaveBeenCalledWith('request failed', {
      authorization: '[REDACTED]',
      metadata: {
        password: '[REDACTED]',
        nonSensitive: 'retained',
        nested: [{ credential: '[REDACTED]' }],
      },
    });

    errorSpy.mockRestore();
  });
});
