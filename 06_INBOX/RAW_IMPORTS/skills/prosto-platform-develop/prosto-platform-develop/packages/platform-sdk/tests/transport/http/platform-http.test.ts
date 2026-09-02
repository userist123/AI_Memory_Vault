import {
  type IPlatformDelegatedIdentity,
  type IPlatformHttpRouteContext,
  type IPlatformHttpRouteContextFactory,
  type IPlatformHttpRouteContextFactoryInput,
  type IPlatformHttpRouteHandler,
  isPlatformDelegatedIdentity,
  PlatformAnonymousIdentity,
  PlatformDelegatedIdentity,
  PlatformHttpContentDisposition,
  PlatformHttpError,
  type PlatformHttpMethodType,
  PlatformHttpRequest,
  PlatformHttpResponse,
  PlatformHttpSetCookie,
  PlatformHttpRouteRegistration,
  PlatformIdentityResolutionRequest,
  type PlatformRequestIdentityType,
} from '@/index.js';
import { describe, expect, it } from 'vitest';

function createAnonymousIdentity(): PlatformRequestIdentityType {
  return new PlatformAnonymousIdentity();
}

function createDelegatedIdentity(
  subjectId = 'user-1',
): IPlatformDelegatedIdentity {
  return new PlatformDelegatedIdentity({
    subjectId,
    roles: ['admin'],
    permissions: ['read:admin'],
  });
}

function createHandler(
  method = 'GET',
  route = '/test',
): IPlatformHttpRouteHandler<IPlatformHttpRouteContext> {
  return {
    method: method as 'GET',
    route,
    async handle(_request, _context) {
      return new PlatformHttpResponse({ status: 200 });
    },
  };
}

function createContextFactory(): IPlatformHttpRouteContextFactory<IPlatformHttpRouteContext> {
  return {
    async create(input: IPlatformHttpRouteContextFactoryInput) {
      return input.baseContext;
    },
  };
}

describe('PlatformHttpRequest', () => {
  it('creates with valid method, path and empty body', () => {
    const request = new PlatformHttpRequest({
      method: 'GET',
      path: '/api/items',
      identity: createAnonymousIdentity(),
    });

    expect(request.method).toBe('GET');
    expect(request.path).toBe('/api/items');
    expect(request.body.variant).toBe('empty');
    expect(request.params).toEqual({});
    expect(request.query).toEqual({});
    expect(request.headers).toEqual({});
  });

  it('accepts all valid HTTP methods', () => {
    const methods = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE', 'HEAD'] as const;

    for (const method of methods) {
      const request = new PlatformHttpRequest({
        method,
        path: '/test',
        identity: createAnonymousIdentity(),
      });

      expect(request.method).toBe(method);
    }
  });

  it('rejects invalid HTTP method', () => {
    expect(
      () =>
        new PlatformHttpRequest({
          method: 'OPTIONS' as PlatformHttpMethodType,
          path: '/test',
          identity: createAnonymousIdentity(),
        }),
    ).toThrowError(/not a valid HTTP method/);
  });

  it('rejects path not starting with "/"', () => {
    expect(
      () =>
        new PlatformHttpRequest({
          method: 'GET',
          path: 'api/items',
          identity: createAnonymousIdentity(),
        }),
    ).toThrowError(/must start with/);
  });

  it('normalizes valid correlation ID', () => {
    const request = new PlatformHttpRequest({
      method: 'GET',
      path: '/test',
      correlationId: 'abc-123_xyz.test=ok+42',
      identity: createAnonymousIdentity(),
    });

    expect(request.correlationId).toBe('abc-123_xyz.test=ok+42');
  });

  it('replaces empty correlation ID with generated UUID', () => {
    const request = new PlatformHttpRequest({
      method: 'GET',
      path: '/test',
      correlationId: '',
      identity: createAnonymousIdentity(),
    });

    expect(request.correlationId).toBeTruthy();
    expect(request.correlationId.length).toBeGreaterThan(0);
  });

  it('replaces oversized correlation ID with generated UUID', () => {
    const request = new PlatformHttpRequest({
      method: 'GET',
      path: '/test',
      correlationId: 'a'.repeat(129),
      identity: createAnonymousIdentity(),
    });

    expect(request.correlationId).toBeTruthy();
    expect(request.correlationId.length).toBe(36);
  });

  it('replaces correlation ID with forbidden chars', () => {
    const request = new PlatformHttpRequest({
      method: 'GET',
      path: '/test',
      correlationId: 'bad id!',
      identity: createAnonymousIdentity(),
    });

    expect(request.correlationId).toBeTruthy();
    expect(request.correlationId.length).toBe(36);
  });

  it('defensive copies params', () => {
    const params = { id: '42' };
    const request = new PlatformHttpRequest({
      method: 'GET',
      path: '/items/:id',
      params,
      identity: createAnonymousIdentity(),
    });

    params.id = '99';

    expect(request.params.id).toBe('42');
    expect(Object.isFrozen(request.params)).toBe(true);
  });

  it('defensive copies query arrays', () => {
    const query = { filter: ['active', 'published'] };
    const request = new PlatformHttpRequest({
      method: 'GET',
      path: '/items',
      query,
      identity: createAnonymousIdentity(),
    });

    query.filter = ['other'];

    expect(request.query.filter).toEqual(['active', 'published']);
  });

  it('defensive copies binary body', () => {
    const data = new Uint8Array([1, 2, 3]);
    const request = new PlatformHttpRequest({
      method: 'POST',
      path: '/upload',
      body: {
        variant: 'binary',
        data,
        contentType: 'application/octet-stream',
      },
      identity: createAnonymousIdentity(),
    });

    data[0] = 99;

    if (request.body.variant === 'binary') {
      expect(request.body.data[0]).toBe(1);
      expect(request.body.data).not.toBe(data);
    }
  });

  it('handles JSON body variant', () => {
    const request = new PlatformHttpRequest({
      method: 'POST',
      path: '/items',
      body: { variant: 'json', data: { name: 'test' } },
      identity: createAnonymousIdentity(),
    });

    expect(request.body.variant).toBe('json');

    if (request.body.variant === 'json') {
      expect(request.body.data).toEqual({ name: 'test' });
    }
  });

  it('handles text body variant', () => {
    const request = new PlatformHttpRequest({
      method: 'POST',
      path: '/items',
      body: { variant: 'text', data: 'hello' },
      identity: createAnonymousIdentity(),
    });

    expect(request.body.variant).toBe('text');

    if (request.body.variant === 'text') {
      expect(request.body.data).toBe('hello');
    }
  });

  it('requires contentType for binary body', () => {
    expect(
      () =>
        new PlatformHttpRequest({
          method: 'POST',
          path: '/upload',
          body: {
            variant: 'binary',
            data: new Uint8Array([1]),
            contentType: '',
          },
          identity: createAnonymousIdentity(),
        }),
    ).toThrowError(/must have a contentType/);
  });

  it('is frozen after construction', () => {
    const request = new PlatformHttpRequest({
      method: 'GET',
      path: '/test',
      identity: createAnonymousIdentity(),
    });

    expect(Object.isFrozen(request)).toBe(true);
  });

  it('stores identity', () => {
    const identity = createDelegatedIdentity();
    const request = new PlatformHttpRequest({
      method: 'GET',
      path: '/test',
      identity,
    });

    expect(request.identity.authenticationType).toBe('delegated');

    if (isPlatformDelegatedIdentity(request.identity)) {
      expect(request.identity.subjectId).toBe('user-1');
    }
  });
});

describe('PlatformHttpResponse', () => {
  it('creates with valid status and empty body', () => {
    const response = new PlatformHttpResponse({ status: 200 });

    expect(response.status).toBe(200);
    expect(response.body.variant).toBe('empty');
    expect(response.headers).toEqual({});
  });

  it('accepts all valid status ranges', () => {
    const validStatuses = [100, 200, 301, 404, 500, 599];

    for (const status of validStatuses) {
      const response = new PlatformHttpResponse({ status });

      expect(response.status).toBe(status);
    }
  });

  it('rejects status below 100', () => {
    expect(() => new PlatformHttpResponse({ status: 99 })).toThrowError(
      PlatformHttpError,
    );

    try {
      new PlatformHttpResponse({ status: 99 });
    } catch (error) {
      expect(error).toMatchObject({ code: 'INVALID_STATUS_CODE' });
    }
  });

  it('rejects status above 599', () => {
    expect(() => new PlatformHttpResponse({ status: 600 })).toThrowError(
      PlatformHttpError,
    );

    try {
      new PlatformHttpResponse({ status: 600 });
    } catch (error) {
      expect(error).toMatchObject({ code: 'INVALID_STATUS_CODE' });
    }
  });

  it('rejects non-integer status', () => {
    expect(() => new PlatformHttpResponse({ status: 200.5 })).toThrowError(
      PlatformHttpError,
    );
  });

  it('rejects body for 204 status', () => {
    expect(
      () =>
        new PlatformHttpResponse({
          status: 204,
          body: { variant: 'json', data: {} },
        }),
    ).toThrowError(PlatformHttpError);

    try {
      new PlatformHttpResponse({
        status: 204,
        body: { variant: 'json', data: {} },
      });
    } catch (error) {
      expect(error).toMatchObject({ code: 'INVALID_BODY_METADATA' });
    }
  });

  it('rejects body for 304 status', () => {
    expect(
      () =>
        new PlatformHttpResponse({
          status: 304,
          body: { variant: 'json', data: {} },
        }),
    ).toThrowError(PlatformHttpError);
  });

  it('rejects Content-Type as custom header', () => {
    expect(
      () =>
        new PlatformHttpResponse({
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
    ).toThrowError(PlatformHttpError);

    try {
      new PlatformHttpResponse({
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    } catch (error) {
      expect(error).toMatchObject({ code: 'INVALID_HEADER_NAME' });
    }
  });

  it('rejects X-Correlation-Id as custom header', () => {
    expect(
      () =>
        new PlatformHttpResponse({
          status: 200,
          headers: { 'X-Correlation-Id': 'abc' },
        }),
    ).toThrowError(PlatformHttpError);
  });

  it('rejects Content-Length as custom header', () => {
    expect(
      () =>
        new PlatformHttpResponse({
          status: 200,
          headers: { 'Content-Length': '100' },
        }),
    ).toThrowError(PlatformHttpError);
  });

  it('rejects Set-Cookie as custom header', () => {
    expect(
      () =>
        new PlatformHttpResponse({
          status: 200,
          headers: { 'Set-Cookie': 'session=abc' },
        }),
    ).toThrowError(PlatformHttpError);
  });

  it('creates immutable validated structured cookie instructions', () => {
    // Arrange
    const cookies = [
      {
        name: '__Host-session',
        value: 'opaque-session-id',
        path: '/',
        httpOnly: true,
        secure: true,
        sameSite: 'strict' as const,
      },
    ];

    // Act
    const response = new PlatformHttpResponse({ status: 302, cookies });
    const originalCookie = cookies[0];

    if (originalCookie === undefined) {
      throw new Error('Expected the test cookie to be present.');
    }

    originalCookie.value = 'mutated';

    // Assert
    expect(response.cookies).toHaveLength(1);
    expect(response.cookies?.[0]).toBeInstanceOf(PlatformHttpSetCookie);
    expect(response.cookies?.[0]).toMatchObject({
      name: '__Host-session',
      value: 'opaque-session-id',
      path: '/',
      httpOnly: true,
      secure: true,
      sameSite: 'strict',
    });
    expect(Object.isFrozen(response.cookies)).toBe(true);
    expect(Object.isFrozen(response.cookies?.[0])).toBe(true);
  });

  it.each([
    {
      scenario: 'a control character in the name',
      cookie: { name: 'session\r\n', value: 'opaque' },
    },
    {
      scenario: 'an invalid cookie value',
      cookie: { name: 'session', value: 'opaque;injected' },
    },
    {
      scenario: 'an invalid path',
      cookie: { name: 'session', value: 'opaque', path: 'relative' },
    },
    {
      scenario: 'an invalid domain',
      cookie: { name: 'session', value: 'opaque', domain: '.example.test' },
    },
    {
      scenario: 'a non-finite expiry',
      cookie: { name: 'session', value: 'opaque', expiresAt: Infinity },
    },
    {
      scenario: 'a fractional max age',
      cookie: { name: 'session', value: 'opaque', maxAge: 1.5 },
    },
    {
      scenario: 'SameSite=None without Secure',
      cookie: { name: 'session', value: 'opaque', sameSite: 'none' as const },
    },
    {
      scenario: '__Secure- without Secure',
      cookie: { name: '__Secure-session', value: 'opaque' },
    },
    {
      scenario: '__Host- without Path=/',
      cookie: { name: '__Host-session', value: 'opaque', secure: true },
    },
  ])('rejects $scenario', ({ cookie }): void => {
    expect(
      () =>
        new PlatformHttpResponse({
          status: 200,
          cookies: [cookie],
        }),
    ).toThrowError(PlatformHttpError);
  });

  it('rejects duplicate cookie tuples', () => {
    expect(
      () =>
        new PlatformHttpResponse({
          status: 200,
          cookies: [
            { name: 'session', value: 'first', path: '/' },
            { name: 'session', value: 'second', path: '/' },
          ],
        }),
    ).toThrowError(PlatformHttpError);
  });

  it('allows a Secure __Host- clearing instruction', () => {
    const response = new PlatformHttpResponse({
      status: 204,
      cookies: [
        {
          name: '__Host-session',
          value: '',
          path: '/',
          maxAge: 0,
          httpOnly: true,
          secure: true,
          sameSite: 'strict',
        },
      ],
    });

    expect(response.cookies?.[0]?.maxAge).toBe(0);
  });

  it('rejects Content-Disposition as custom header', () => {
    expect(
      () =>
        new PlatformHttpResponse({
          status: 200,
          headers: { 'Content-Disposition': 'attachment' },
        }),
    ).toThrowError(PlatformHttpError);
  });

  it('allows valid custom headers', () => {
    const response = new PlatformHttpResponse({
      status: 200,
      headers: { 'X-Custom': 'value', 'Cache-Control': 'no-cache' },
    });

    expect(response.headers['X-Custom']).toBe('value');
    expect(response.headers['Cache-Control']).toBe('no-cache');
  });

  it('defensive copies custom headers', () => {
    const headers = { 'X-Custom': 'original' };
    const response = new PlatformHttpResponse({
      status: 200,
      headers,
    });

    headers['X-Custom'] = 'modified';

    expect(response.headers['X-Custom']).toBe('original');
  });

  it('handles json response body', () => {
    const response = new PlatformHttpResponse({
      status: 200,
      body: { variant: 'json', data: { result: 'ok' } },
    });

    expect(response.body.variant).toBe('json');

    if (response.body.variant === 'json') {
      expect(response.body.data).toEqual({ result: 'ok' });
    }
  });

  it('handles binary response body', () => {
    const data = new Uint8Array([10, 20, 30]);
    const response = new PlatformHttpResponse({
      status: 200,
      body: { variant: 'binary', data, contentType: 'image/png' },
    });

    expect(response.body.variant).toBe('binary');

    if (response.body.variant === 'binary') {
      expect(response.body.contentType).toBe('image/png');
      expect(response.body.data).toBe(data);
    }
  });

  it('requires contentType for binary response body', () => {
    expect(
      () =>
        new PlatformHttpResponse({
          status: 200,
          body: {
            variant: 'binary',
            data: new Uint8Array([1]),
            contentType: '',
          },
        }),
    ).toThrowError(PlatformHttpError);
  });

  it('handles stream response body', () => {
    const stream = new ReadableStream<Uint8Array>();
    const response = new PlatformHttpResponse({
      status: 200,
      body: {
        variant: 'stream',
        stream,
        contentType: 'application/octet-stream',
      },
    });

    expect(response.body.variant).toBe('stream');

    if (response.body.variant === 'stream') {
      expect(response.body.stream).toBe(stream);
      expect(response.body.contentType).toBe('application/octet-stream');
    }
  });

  it('requires contentType for stream response body', () => {
    const stream = new ReadableStream<Uint8Array>();

    expect(
      () =>
        new PlatformHttpResponse({
          status: 200,
          body: { variant: 'stream', stream, contentType: '' },
        }),
    ).toThrowError(PlatformHttpError);
  });

  it('is frozen after construction', () => {
    const response = new PlatformHttpResponse({ status: 200 });

    expect(Object.isFrozen(response)).toBe(true);
  });
});

describe('PlatformIdentityResolutionRequest', () => {
  it('creates with required fields', () => {
    const request = new PlatformIdentityResolutionRequest({
      correlationId: 'corr-123',
      method: 'GET',
      path: '/api/test',
    });

    expect(request.correlationId).toBe('corr-123');
    expect(request.method).toBe('GET');
    expect(request.path).toBe('/api/test');
    expect(request.headers).toEqual({});
    expect(request.params).toEqual({});
    expect(request.query).toEqual({});
  });

  it('defensive copies headers', () => {
    const headers = { Authorization: ['Bearer token'] };
    const request = new PlatformIdentityResolutionRequest({
      correlationId: 'corr-123',
      method: 'GET',
      path: '/test',
      headers,
    });

    headers.Authorization = ['modified'];

    expect(request.headers.Authorization).toEqual(['Bearer token']);
  });

  it('defensive copies params', () => {
    const params = { id: '42' };
    const request = new PlatformIdentityResolutionRequest({
      correlationId: 'corr-123',
      method: 'GET',
      path: '/test',
      params,
    });

    params.id = '99';

    expect(request.params.id).toBe('42');
  });

  it('is frozen after construction', () => {
    const request = new PlatformIdentityResolutionRequest({
      correlationId: 'corr-123',
      method: 'GET',
      path: '/test',
    });

    expect(Object.isFrozen(request)).toBe(true);
  });
});

describe('PlatformAnonymousIdentity', () => {
  it('creates with default empty roles and permissions', () => {
    const identity = new PlatformAnonymousIdentity();

    expect(identity.authenticationType).toBe('anonymous');
    expect(identity.roles).toEqual([]);
    expect(identity.permissions).toEqual([]);
  });

  it('creates with given roles and permissions', () => {
    const identity = new PlatformAnonymousIdentity({
      roles: ['viewer'],
      permissions: ['read'],
    });

    expect(identity.roles).toEqual(['viewer']);
    expect(identity.permissions).toEqual(['read']);
  });

  it('defensive copies roles and permissions', () => {
    const roles = ['viewer'];
    const permissions = ['read'];
    const identity = new PlatformAnonymousIdentity({ roles, permissions });

    roles.push('editor');
    permissions.push('write');

    expect(identity.roles).toEqual(['viewer']);
    expect(identity.permissions).toEqual(['read']);
  });

  it('is frozen after construction', () => {
    const identity = new PlatformAnonymousIdentity();

    expect(Object.isFrozen(identity)).toBe(true);
  });
});

describe('PlatformDelegatedIdentity', () => {
  it('creates with valid subjectId', () => {
    const identity = new PlatformDelegatedIdentity({
      subjectId: 'user-1',
      roles: ['admin'],
      permissions: ['write'],
    });

    expect(identity.authenticationType).toBe('delegated');
    expect(identity.subjectId).toBe('user-1');
    expect(identity.roles).toEqual(['admin']);
    expect(identity.permissions).toEqual(['write']);
  });

  it('rejects empty subjectId', () => {
    expect(() => new PlatformDelegatedIdentity({ subjectId: '' })).toThrowError(
      PlatformHttpError,
    );

    try {
      new PlatformDelegatedIdentity({ subjectId: '' });
    } catch (error) {
      expect(error).toMatchObject({ code: 'HTTP_UNAUTHENTICATED' });
    }
  });

  it('rejects whitespace-only subjectId', () => {
    expect(
      () => new PlatformDelegatedIdentity({ subjectId: '   ' }),
    ).toThrowError(PlatformHttpError);
  });

  it('defensive copies roles and permissions', () => {
    const roles = ['admin'];
    const permissions = ['write'];
    const identity = new PlatformDelegatedIdentity({
      subjectId: 'user-1',
      roles,
      permissions,
    });

    roles.push('editor');
    permissions.push('read');

    expect(identity.roles).toEqual(['admin']);
    expect(identity.permissions).toEqual(['write']);
  });

  it('is frozen after construction', () => {
    const identity = new PlatformDelegatedIdentity({ subjectId: 'user-1' });

    expect(Object.isFrozen(identity)).toBe(true);
  });
});

describe('PlatformHttpRouteRegistration', () => {
  it('creates valid registration', () => {
    const handler = createHandler();
    const factory = createContextFactory();
    const registration = new PlatformHttpRouteRegistration(handler, factory);

    expect(registration.method).toBe('GET');
    expect(registration.route).toBe('/test');
  });

  it('validates route grammar — rejects missing leading slash', () => {
    const handler = createHandler('GET', 'test');
    const factory = createContextFactory();

    expect(
      () => new PlatformHttpRouteRegistration(handler, factory),
    ).toThrowError(PlatformHttpError);

    try {
      new PlatformHttpRouteRegistration(handler, factory);
    } catch (error) {
      expect(error).toMatchObject({ code: 'INVALID_ROUTE_GRAMMAR' });
    }
  });

  it('validates route grammar — rejects trailing slash', () => {
    const handler = createHandler('GET', '/test/');
    const factory = createContextFactory();

    expect(
      () => new PlatformHttpRouteRegistration(handler, factory),
    ).toThrowError(PlatformHttpError);
  });

  it('validates route grammar — rejects empty segments', () => {
    const handler = createHandler('GET', '//');
    const factory = createContextFactory();

    expect(
      () => new PlatformHttpRouteRegistration(handler, factory),
    ).toThrowError(PlatformHttpError);
  });

  it('validates route grammar — accepts named params', () => {
    const handler = createHandler('GET', '/items/:id');
    const factory = createContextFactory();
    const registration = new PlatformHttpRouteRegistration(handler, factory);

    expect(registration.route).toBe('/items/:id');
  });

  it('validates route grammar — rejects invalid param name with special chars', () => {
    const handler = createHandler('GET', '/items/:id-name');
    const factory = createContextFactory();

    expect(
      () => new PlatformHttpRouteRegistration(handler, factory),
    ).toThrowError(PlatformHttpError);
  });

  it('validates route grammar — rejects invalid param name starting with digit', () => {
    const handler = createHandler('GET', '/items/:1id');
    const factory = createContextFactory();

    expect(
      () => new PlatformHttpRouteRegistration(handler, factory),
    ).toThrowError(PlatformHttpError);
  });

  it('validates route grammar — accepts multiple params', () => {
    const handler = createHandler('GET', '/sections/:sectionId/items/:itemId');
    const factory = createContextFactory();
    const registration = new PlatformHttpRouteRegistration(handler, factory);

    expect(registration.route).toBe('/sections/:sectionId/items/:itemId');
  });

  it('execute delegates to handler through context factory', async () => {
    let capturedContext: IPlatformHttpRouteContext | undefined;
    const handler: IPlatformHttpRouteHandler<IPlatformHttpRouteContext> = {
      method: 'GET',
      route: '/test',
      async handle(request, context) {
        capturedContext = context;

        return new PlatformHttpResponse({ status: 200 });
      },
    };

    const factory: IPlatformHttpRouteContextFactory<IPlatformHttpRouteContext> =
      {
        async create(input) {
          return input.baseContext;
        },
      };

    const registration = new PlatformHttpRouteRegistration(handler, factory);
    const request = new PlatformHttpRequest({
      method: 'GET',
      path: '/test',
      identity: createAnonymousIdentity(),
    });

    const baseContext: IPlatformHttpRouteContext = {
      correlationId: 'corr-1',
      identity: createAnonymousIdentity(),
      signal: new AbortController().signal,
    };

    const response = await registration.execute({ request, baseContext });

    expect(response.status).toBe(200);
    expect(capturedContext).toBe(baseContext);
  });
});

describe('PlatformHttpContentDisposition', () => {
  it('creates with inline type', () => {
    const cd = new PlatformHttpContentDisposition({ type: 'inline' });

    expect(cd.type).toBe('inline');
    expect(cd.filename).toBeUndefined();
  });

  it('creates with attachment type and filename', () => {
    const cd = new PlatformHttpContentDisposition({
      type: 'attachment',
      filename: 'report.pdf',
    });

    expect(cd.type).toBe('attachment');
    expect(cd.filename).toBe('report.pdf');
    expect(cd.safeFilename).toBe('report.pdf');
    expect(cd.rfc5987Filename).toContain('report.pdf');
  });

  it('rejects invalid disposition type', () => {
    expect(
      () =>
        new PlatformHttpContentDisposition({
          type: 'form-data' as 'inline',
        }),
    ).toThrowError(PlatformHttpError);
  });

  it('rejects filename with control characters', () => {
    expect(
      () =>
        new PlatformHttpContentDisposition({
          type: 'attachment',
          filename: 'file\r\n.txt',
        }),
    ).toThrowError(PlatformHttpError);
  });

  it('rejects filename with null character', () => {
    expect(
      () =>
        new PlatformHttpContentDisposition({
          type: 'attachment',
          filename: 'file\x00.txt',
        }),
    ).toThrowError(PlatformHttpError);
  });

  it('is frozen after construction', () => {
    const cd = new PlatformHttpContentDisposition({ type: 'inline' });

    expect(Object.isFrozen(cd)).toBe(true);
  });
});

describe('PlatformHttpError', () => {
  it('creates with code and message', () => {
    const error = new PlatformHttpError(
      'INVALID_HTTP_METHOD',
      'Invalid method.',
    );

    expect(error.code).toBe('INVALID_HTTP_METHOD');
    expect(error.message).toBe('Invalid method.');
    expect(error.name).toBe('PlatformHttpError');
  });

  it('creates with code, message and details', () => {
    const error = new PlatformHttpError(
      'INVALID_STATUS_CODE',
      'Status out of range.',
      { status: 999 },
    );

    expect(error.code).toBe('INVALID_STATUS_CODE');
    expect(error.details).toEqual({ status: 999 });
  });
});

describe('isPlatformDelegatedIdentity', () => {
  it('returns true for delegated identity', () => {
    const identity = createDelegatedIdentity();

    expect(isPlatformDelegatedIdentity(identity)).toBe(true);
  });

  it('returns false for anonymous identity', () => {
    const identity = createAnonymousIdentity();

    expect(isPlatformDelegatedIdentity(identity)).toBe(false);
  });
});
