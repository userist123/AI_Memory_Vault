import { describe, expect, it } from 'vitest';
import {
  AdminBffHostConfigurationError,
  parseBearerAuthConfig,
} from '@/config/auth-config.js';
import { parseKeyRingConfig } from '@/config/key-ring-config.js';
import {
  AdminBffHostConfigurationError as HostConfigurationError,
  parseAdminBffHostConfiguration,
} from '@/config/host-config.js';
import { parseSessionConfig } from '@/config/session-config.js';

const key = Buffer.alloc(32, 7).toString('base64url');

function createEnvironment(): NodeJS.ProcessEnv {
  return {
    ADMIN_BFF_AUTH_ISSUER: 'https://issuer.example.test',
    ADMIN_BFF_AUTH_JWKS_URI: 'https://issuer.example.test/jwks',
    ADMIN_BFF_AUTH_AUDIENCES_JSON: '["https://api.example.test"]',
    ADMIN_BFF_AUTH_ALLOWED_ALGORITHMS_JSON: '["PS256"]',
    ADMIN_BFF_OIDC_AUTHORIZATION_ENDPOINT:
      'https://issuer.example.test/authorize',
    ADMIN_BFF_OIDC_TOKEN_ENDPOINT: 'https://issuer.example.test/token',
    ADMIN_BFF_OIDC_REVOCATION_ENDPOINT: 'https://issuer.example.test/revoke',
    ADMIN_BFF_OIDC_REDIRECT_URI: 'https://admin.example.test/auth/callback',
    ADMIN_BFF_OIDC_CLIENT_ID: 'admin-bff',
    ADMIN_BFF_OIDC_CLIENT_SECRET: 'injected-secret',
    ADMIN_BFF_OIDC_SCOPES_JSON: '["openid","offline_access"]',
    ADMIN_BFF_SESSION_COOKIE_VERSION: '2',
    ADMIN_BFF_SESSION_KEY_RING_JSON: JSON.stringify({
      activeKeyId: 'active',
      keys: [{ id: 'active', key }],
    }),
  };
}

describe('Admin BFF host configuration parsers', (): void => {
  it('parses bearer, session, and key-ring configuration without importing main', (): void => {
    // Arrange
    const environment = createEnvironment();

    // Act
    const bearer = parseBearerAuthConfig(environment);
    const session = parseSessionConfig(environment);
    const keyRing = parseKeyRingConfig(environment);

    // Assert
    expect(bearer.allowedAlgorithms).toEqual(['PS256']);
    expect(session.allowedAlgorithms).toEqual(['PS256']);
    expect(session.cookieVersion).toBe(2);
    expect(keyRing.activeKeyId).toBe('active');
  });

  it('returns one deterministic error without exposing an injected key', (): void => {
    // Arrange
    const environment = createEnvironment();
    environment.ADMIN_BFF_SESSION_KEY_RING_JSON =
      '{"activeKeyId":"secret-key"}';

    // Act
    const parse = (): ReturnType<typeof parseKeyRingConfig> =>
      parseKeyRingConfig(environment);

    // Assert
    expect(parse).toThrow(AdminBffHostConfigurationError);
    expect(parse).toThrow('Admin BFF host configuration is invalid.');
  });

  it('rejects malformed bearer configuration through the same safe error', (): void => {
    // Arrange
    const environment = createEnvironment();
    environment.ADMIN_BFF_AUTH_AUDIENCES_JSON = 'not-json';

    // Act
    const parse = (): void => {
      parseBearerAuthConfig(environment);
    };

    // Assert
    expect(parse).toThrow(AdminBffHostConfigurationError);
    expect(parse).toThrow('Admin BFF host configuration is invalid.');
  });

  it('uses local defaults without requiring OIDC configuration', (): void => {
    // Arrange
    const environment: NodeJS.ProcessEnv = { ADMIN_BFF_AUTH_MODE: 'local' };

    // Act
    const configuration = parseAdminBffHostConfiguration(environment);

    // Assert
    expect(configuration.auth).toEqual({
      mode: 'local',
      local: {
        origin: 'http://127.0.0.1:3001',
        secureCookies: false,
      },
    });
    expect(configuration.configDir).toBe('./config');
    expect(configuration.http).toEqual({ host: '127.0.0.1', port: 3001 });
  });

  it('rejects public plaintext local authentication without exposing configuration', (): void => {
    // Arrange
    const environment: NodeJS.ProcessEnv = {
      ADMIN_BFF_AUTH_MODE: 'local',
      ADMIN_BFF_PUBLIC_ORIGIN: 'http://admin.example.test',
    };

    // Act
    const parse = (): ReturnType<typeof parseAdminBffHostConfiguration> =>
      parseAdminBffHostConfiguration(environment);

    // Assert
    expect(parse).toThrow(HostConfigurationError);
    expect(parse).toThrow('Admin BFF host configuration is invalid.');
  });
});
