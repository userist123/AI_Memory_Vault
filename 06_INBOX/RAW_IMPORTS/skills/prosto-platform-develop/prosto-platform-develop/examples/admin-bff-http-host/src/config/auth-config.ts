import type {
  IPlatformOidcBearerResolverConfig,
  PlatformAuthJwtAlgorithmType,
} from '@prosto/platform-adapter-auth-oidc';

const ALLOWED_ALGORITHMS = new Set<PlatformAuthJwtAlgorithmType>([
  'RS256',
  'PS256',
  'ES256',
]);

/** @internal */
export class AdminBffHostConfigurationError extends Error {
  constructor() {
    super('Admin BFF host configuration is invalid.');
    this.name = 'AdminBffHostConfigurationError';
  }
}

/** @internal */
export function parseBearerAuthConfig(
  environment: NodeJS.ProcessEnv,
): IPlatformOidcBearerResolverConfig {
  try {
    const allowedAlgorithms = environment.ADMIN_BFF_AUTH_ALLOWED_ALGORITHMS_JSON
      ? parseAlgorithms(environment.ADMIN_BFF_AUTH_ALLOWED_ALGORITHMS_JSON)
      : undefined;

    return Object.freeze({
      issuer: requiredValue(environment.ADMIN_BFF_AUTH_ISSUER),
      jwksUri: requiredValue(environment.ADMIN_BFF_AUTH_JWKS_URI),
      audiences: parseStringArray(
        requiredValue(environment.ADMIN_BFF_AUTH_AUDIENCES_JSON),
      ),
      ...(allowedAlgorithms !== undefined && { allowedAlgorithms }),
      ...(environment.ADMIN_BFF_AUTH_ROLES_CLAIM !== undefined && {
        rolesClaim: requiredValue(environment.ADMIN_BFF_AUTH_ROLES_CLAIM),
      }),
      ...(environment.ADMIN_BFF_AUTH_PERMISSIONS_CLAIM !== undefined && {
        permissionsClaim: requiredValue(
          environment.ADMIN_BFF_AUTH_PERMISSIONS_CLAIM,
        ),
      }),
    });
  } catch {
    throw new AdminBffHostConfigurationError();
  }
}

/** @internal */
export function parseStringArray(value: string): readonly string[] {
  const parsed: unknown = JSON.parse(value);

  if (
    !Array.isArray(parsed) ||
    !parsed.every((entry) => typeof entry === 'string')
  ) {
    throw new Error('Invalid string array.');
  }

  return Object.freeze([...parsed]);
}

function parseAlgorithms(
  value: string,
): readonly PlatformAuthJwtAlgorithmType[] {
  const parsed = parseStringArray(value);

  if (
    !parsed.every((algorithm) =>
      ALLOWED_ALGORITHMS.has(algorithm as PlatformAuthJwtAlgorithmType),
    )
  ) {
    throw new Error('Invalid algorithms.');
  }

  return Object.freeze(parsed as PlatformAuthJwtAlgorithmType[]);
}

function requiredValue(value: string | undefined): string {
  if (typeof value !== 'string' || !value.length || value.trim() !== value) {
    throw new Error('Missing configuration value.');
  }

  return value;
}
