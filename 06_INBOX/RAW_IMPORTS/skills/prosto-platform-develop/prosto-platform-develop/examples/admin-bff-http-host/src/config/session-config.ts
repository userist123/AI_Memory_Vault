import type { IPlatformAuthOidcSessionModuleConfig } from '@prosto/platform-module-auth-oidc-session';
import {
  AdminBffHostConfigurationError,
  parseBearerAuthConfig,
  parseStringArray,
} from './auth-config.js';

type SessionModuleEnvironmentConfigType = Omit<
  IPlatformAuthOidcSessionModuleConfig,
  'cipher' | 'accessTokenResolver'
>;

/** @internal */
export function parseSessionConfig(
  environment: NodeJS.ProcessEnv,
): SessionModuleEnvironmentConfigType {
  try {
    const bearerConfig = parseBearerAuthConfig(environment);
    const cookieVersion = environment.ADMIN_BFF_SESSION_COOKIE_VERSION
      ? parsePositiveInteger(environment.ADMIN_BFF_SESSION_COOKIE_VERSION)
      : undefined;
    const resource = environment.ADMIN_BFF_OIDC_RESOURCE;

    return Object.freeze({
      issuer: bearerConfig.issuer,
      jwksUri: bearerConfig.jwksUri,
      authorizationEndpoint: requiredValue(
        environment.ADMIN_BFF_OIDC_AUTHORIZATION_ENDPOINT,
      ),
      tokenEndpoint: requiredValue(environment.ADMIN_BFF_OIDC_TOKEN_ENDPOINT),
      revocationEndpoint: requiredValue(
        environment.ADMIN_BFF_OIDC_REVOCATION_ENDPOINT,
      ),
      redirectUri: requiredValue(environment.ADMIN_BFF_OIDC_REDIRECT_URI),
      clientId: requiredValue(environment.ADMIN_BFF_OIDC_CLIENT_ID),
      clientSecret: requiredValue(environment.ADMIN_BFF_OIDC_CLIENT_SECRET),
      scopes: parseStringArray(
        requiredValue(environment.ADMIN_BFF_OIDC_SCOPES_JSON),
      ),
      audiences: bearerConfig.audiences,
      ...(bearerConfig.allowedAlgorithms !== undefined && {
        allowedAlgorithms: bearerConfig.allowedAlgorithms,
      }),
      ...(resource !== undefined && { resource: requiredValue(resource) }),
      ...(cookieVersion !== undefined && { cookieVersion }),
    });
  } catch {
    throw new AdminBffHostConfigurationError();
  }
}

function requiredValue(value: string | undefined): string {
  if (typeof value !== 'string' || !value.length || value.trim() !== value) {
    throw new Error('Missing configuration value.');
  }

  return value;
}

function parsePositiveInteger(value: string): number {
  if (!/^[1-9][0-9]*$/u.test(value)) {
    throw new Error('Invalid cookie version.');
  }

  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed)) {
    throw new Error('Invalid cookie version.');
  }

  return parsed;
}
