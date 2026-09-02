import {
  type CryptoKey,
  exportJWK,
  generateKeyPair,
  type JWTPayload,
  SignJWT,
} from 'jose';
import {
  afterEach,
  beforeAll,
  beforeEach,
  describe,
  expect,
  it,
  vi,
} from 'vitest';
import {
  type IPlatformAuthLogger,
  type IPlatformOidcBearerResolverConfig,
  PlatformAuthConfigurationError,
  type PlatformAuthJwtAlgorithmType,
  PlatformOidcBearerResolver,
} from '@/index.js';
import type { IPlatformIdentityResolutionRequest } from '@prosto/platform-sdk';

const ISSUER = 'https://identity.example.test';
const JWKS_URI = 'https://identity.example.test/keys.json';
const AUDIENCE = 'prosto-admin-api';

interface IKeyFixture {
  readonly algorithm: PlatformAuthJwtAlgorithmType;
  readonly kid: string;
  readonly privateKey: CryptoKey;
  readonly jwk: Record<string, unknown>;
}

let keyFixtures: readonly IKeyFixture[];
let fetchMock: ReturnType<typeof vi.fn>;

beforeAll(async (): Promise<void> => {
  keyFixtures = await Promise.all(
    (['RS256', 'PS256', 'ES256'] as const).map(async (algorithm) => {
      const { privateKey, publicKey } = await generateKeyPair(algorithm);
      const kid = `test-${algorithm.toLowerCase()}`;
      const jwk = await exportJWK(publicKey);

      return {
        algorithm,
        kid,
        privateKey,
        jwk: { ...jwk, alg: algorithm, kid, use: 'sig' },
      };
    }),
  );
});

beforeEach((): void => {
  fetchMock = vi.fn(async () => createJwksResponse());
  vi.stubGlobal('fetch', fetchMock);
});

afterEach((): void => {
  vi.unstubAllGlobals();
});

function createConfig(
  overrides: Partial<IPlatformOidcBearerResolverConfig> = {},
): IPlatformOidcBearerResolverConfig {
  return {
    issuer: ISSUER,
    jwksUri: JWKS_URI,
    audiences: [AUDIENCE],
    ...overrides,
  };
}

function createRequest(
  authorizationHeaders?: readonly string[],
): IPlatformIdentityResolutionRequest {
  return {
    correlationId: 'correlation-42',
    method: 'GET',
    path: '/admin/plugins',
    headers:
      authorizationHeaders === undefined
        ? {}
        : { authorization: authorizationHeaders },
    params: {},
    query: {},
  };
}

function createJwksResponse(): Response {
  return new Response(
    JSON.stringify({ keys: keyFixtures.map(({ jwk }) => jwk) }),
    {
      status: 200,
      headers: { 'content-type': 'application/json' },
    },
  );
}

function captureError(action: () => void): unknown {
  try {
    action();
  } catch (error: unknown) {
    return error;
  }

  throw new Error('Expected action to throw.');
}

async function createToken(
  algorithm: PlatformAuthJwtAlgorithmType = 'RS256',
  payload: JWTPayload = {},
  options: { readonly issuer?: string; readonly audience?: string } = {},
): Promise<string> {
  const fixture = keyFixtures.find(
    (candidate) => candidate.algorithm === algorithm,
  );
  if (fixture === undefined) {
    throw new Error('Missing local test key fixture.');
  }

  return new SignJWT(payload)
    .setProtectedHeader({ alg: algorithm, kid: fixture.kid })
    .setIssuedAt()
    .setIssuer(options.issuer ?? ISSUER)
    .setAudience(options.audience ?? AUDIENCE)
    .setExpirationTime('10m')
    .sign(fixture.privateKey);
}

describe('PlatformOidcBearerResolver', (): void => {
  it('returns anonymous without an authorization header', async (): Promise<void> => {
    // Arrange
    const resolver = new PlatformOidcBearerResolver(createConfig());

    // Act
    const identity = await resolver.resolve(createRequest());

    // Assert
    expect(identity).toEqual({
      authenticationType: 'anonymous',
      roles: [],
      permissions: [],
    });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each(['RS256', 'PS256', 'ES256'] as const)(
    'verifies a valid %s token with bounded delegated claims',
    async (algorithm: PlatformAuthJwtAlgorithmType): Promise<void> => {
      // Arrange
      const resolver = new PlatformOidcBearerResolver(
        createConfig({ allowedAlgorithms: [algorithm] }),
      );
      const token = await createToken(algorithm, {
        sub: 'operator-42',
        roles: ['admin'],
        permissions: ['plugins:read'],
      });

      // Act
      const identity = await resolver.resolve(
        createRequest([`Bearer ${token}`]),
      );

      // Assert
      expect(identity).toEqual({
        authenticationType: 'delegated',
        subjectId: 'operator-42',
        roles: ['admin'],
        permissions: ['plugins:read'],
      });
    },
  );

  it('rejects malformed headers and bearer credentials over 12 KiB', async (): Promise<void> => {
    // Arrange
    const resolver = new PlatformOidcBearerResolver(createConfig());
    const oversizedToken = 'a'.repeat(12 * 1024 + 1);

    // Act and assert
    await expect(
      resolver.resolve(createRequest(['Basic value'])),
    ).rejects.toMatchObject({
      code: 'HTTP_UNAUTHENTICATED',
    });
    await expect(
      resolver.resolve(createRequest(['Bearer first', 'Bearer second'])),
    ).rejects.toMatchObject({ code: 'HTTP_UNAUTHENTICATED' });
    await expect(
      resolver.resolve(createRequest([`Bearer ${oversizedToken}`])),
    ).rejects.toMatchObject({ code: 'HTTP_UNAUTHENTICATED' });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('rejects invalid issuer, audience, signature, temporal, and claim values', async (): Promise<void> => {
    // Arrange
    const resolver = new PlatformOidcBearerResolver(createConfig());
    const invalidIssuer = await createToken(
      'RS256',
      { sub: 'operator-42' },
      {
        issuer: 'https://other-identity.example.test',
      },
    );
    const invalidAudience = await createToken(
      'RS256',
      { sub: 'operator-42' },
      {
        audience: 'other-api',
      },
    );
    const malformedClaims = await createToken('RS256', {
      sub: 'operator-42',
      roles: ['admin', ' admin '],
    });
    const oversizedSubject = await createToken('RS256', {
      sub: 's'.repeat(256),
    });
    const blankSubject = await createToken('RS256', { sub: '   ' });
    const forgedKey = await generateKeyPair('RS256');
    const primaryFixture = keyFixtures[0];
    if (primaryFixture === undefined) {
      throw new Error('Missing local RS256 test key fixture.');
    }
    const forgedSignature = await new SignJWT({ sub: 'operator-42' })
      .setProtectedHeader({ alg: 'RS256', kid: primaryFixture.kid })
      .setIssuer(ISSUER)
      .setAudience(AUDIENCE)
      .setIssuedAt()
      .setExpirationTime('10m')
      .sign(forgedKey.privateKey);
    const expired = await new SignJWT({ sub: 'operator-42' })
      .setProtectedHeader({ alg: 'RS256', kid: primaryFixture.kid })
      .setIssuer(ISSUER)
      .setAudience(AUDIENCE)
      .setIssuedAt()
      .setExpirationTime('0s')
      .sign(primaryFixture.privateKey);

    // Act and assert
    await expect(
      resolver.resolve(createRequest([`Bearer ${invalidIssuer}`])),
    ).rejects.toMatchObject({ code: 'HTTP_UNAUTHENTICATED' });
    await expect(
      resolver.resolve(createRequest([`Bearer ${invalidAudience}`])),
    ).rejects.toMatchObject({ code: 'HTTP_UNAUTHENTICATED' });
    await expect(
      resolver.resolve(createRequest([`Bearer ${malformedClaims}`])),
    ).rejects.toMatchObject({ code: 'HTTP_UNAUTHENTICATED' });
    await expect(
      resolver.resolve(createRequest([`Bearer ${oversizedSubject}`])),
    ).rejects.toMatchObject({ code: 'HTTP_UNAUTHENTICATED' });
    await expect(
      resolver.resolve(createRequest([`Bearer ${blankSubject}`])),
    ).rejects.toMatchObject({ code: 'HTTP_UNAUTHENTICATED' });
    await expect(
      resolver.resolve(createRequest([`Bearer ${forgedSignature}`])),
    ).rejects.toMatchObject({ code: 'HTTP_UNAUTHENTICATED' });
    await expect(
      resolver.resolve(createRequest([`Bearer ${expired}`])),
    ).rejects.toMatchObject({ code: 'HTTP_UNAUTHENTICATED' });
    await expect(
      resolver.resolve(createRequest(['Bearer a.b.c'])),
    ).rejects.toMatchObject({
      code: 'HTTP_UNAUTHENTICATED',
    });
  });

  it('uses one cached remote JWKS and denies redirects for every retrieval', async (): Promise<void> => {
    // Arrange
    const resolver = new PlatformOidcBearerResolver(createConfig());
    const token = await createToken('RS256', { sub: 'operator-42' });

    // Act
    await Promise.all([
      resolver.resolve(createRequest([`Bearer ${token}`])),
      resolver.resolve(createRequest([`Bearer ${token}`])),
    ]);

    // Assert
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({ redirect: 'error' });
  });

  it('maps JWKS transport failures to a safe unavailable error', async (): Promise<void> => {
    // Arrange
    fetchMock.mockRejectedValueOnce(new TypeError('network unavailable'));
    const resolver = new PlatformOidcBearerResolver(createConfig());
    const token = await createToken('RS256', { sub: 'operator-42' });

    // Act and assert
    await expect(
      resolver.resolve(createRequest([`Bearer ${token}`])),
    ).rejects.toMatchObject({
      code: 'IDENTITY_RESOLUTION_UNAVAILABLE',
      message: 'Bearer identity resolution is temporarily unavailable.',
    });
  });

  it('emits only redacted authentication outcomes', async (): Promise<void> => {
    // Arrange
    const logger: IPlatformAuthLogger = { log: vi.fn() };
    const resolver = new PlatformOidcBearerResolver(createConfig(), logger);
    const token = await createToken('RS256', {
      sub: 'operator-42',
      roles: ['admin'],
      permissions: ['plugins:read'],
    });

    // Act
    await resolver.resolve(createRequest([`Bearer ${token}`]));

    // Assert
    expect(logger.log).toHaveBeenCalledWith({
      event: 'bearer_identity_resolution',
      correlationId: 'correlation-42',
      outcome: 'authenticated',
      durationMs: expect.any(Number),
    });
    const serializedEvent = JSON.stringify(
      vi.mocked(logger.log).mock.calls[0]?.[0],
    );
    expect(serializedEvent).not.toContain('operator-42');
    expect(serializedEvent).not.toContain('plugins:read');
    expect(serializedEvent).not.toContain(token);
  });

  it('rejects unsafe configuration before any request', (): void => {
    // Act and assert
    expect(
      captureError(() => new PlatformOidcBearerResolver(undefined as never)),
    ).toMatchObject({ code: 'INVALID_ISSUER' });
    expect(
      captureError(
        () =>
          new PlatformOidcBearerResolver(
            createConfig({ issuer: 'http://identity.example.test' }),
          ),
      ),
    ).toMatchObject({ code: 'INVALID_ISSUER' });
    expect(
      () =>
        new PlatformOidcBearerResolver(
          createConfig({
            audiences: [],
            allowedAlgorithms: ['HS256' as never],
          }),
        ),
    ).toThrow(PlatformAuthConfigurationError);
    expect(
      captureError(
        () =>
          new PlatformOidcBearerResolver(
            createConfig({ audiences: [AUDIENCE, AUDIENCE] }),
          ),
      ),
    ).toMatchObject({ code: 'INVALID_AUDIENCES' });
    expect(
      captureError(
        () =>
          new PlatformOidcBearerResolver(
            createConfig({ rolesClaim: 'roles', permissionsClaim: 'roles' }),
          ),
      ),
    ).toMatchObject({ code: 'INVALID_CLAIM_NAME' });
    expect(
      captureError(
        () =>
          new PlatformOidcBearerResolver(
            createConfig({ jwksTimeoutMs: 5_001 }),
          ),
      ),
    ).toMatchObject({ code: 'INVALID_JWKS_DURATION' });
  });
});
