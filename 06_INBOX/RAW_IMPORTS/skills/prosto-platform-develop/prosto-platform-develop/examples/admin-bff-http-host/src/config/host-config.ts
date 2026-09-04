import type { IPlatformLocalAuthSessionModuleConfig } from '@prosto/platform-module-auth-local-session';

type LocalAuthenticationConfigType = Pick<
  IPlatformLocalAuthSessionModuleConfig,
  | 'origin'
  | 'secureCookies'
  | 'sessionIdleTtlMs'
  | 'sessionAbsoluteTtlMs'
  | 'minimumPasswordLength'
>;

type AdminBffAuthenticationConfigurationType =
  | {
      readonly mode: 'local';
      readonly local: LocalAuthenticationConfigType;
    }
  | {
      readonly mode: 'oidc';
    };

/** @internal */
export interface IAdminBffHostConfiguration {
  readonly auth: AdminBffAuthenticationConfigurationType;
  readonly configDir: string;
  readonly environment: string;
  readonly http: {
    readonly host: string;
    readonly port: number;
  };
}

/** @internal */
export function parseAdminBffHostConfiguration(
  environment: NodeJS.ProcessEnv,
): IAdminBffHostConfiguration {
  try {
    const mode = parseAuthenticationMode(environment.ADMIN_BFF_AUTH_MODE);
    const http = {
      host: parseHost(environment.ADMIN_BFF_HTTP_HOST),
      port: parsePort(environment.ADMIN_BFF_HTTP_PORT),
    };
    const configDir = parseConfigDirectory(environment.ADMIN_BFF_CONFIG_DIR);
    const auth: AdminBffAuthenticationConfigurationType =
      mode === 'local'
        ? { mode, local: parseLocalAuthenticationConfig(environment, http) }
        : { mode };
    const parsed: IAdminBffHostConfiguration = {
      auth,
      configDir,
      environment: parseEnvironment(environment.NODE_ENV),
      http,
    };

    return Object.freeze(parsed);
  } catch {
    throw new AdminBffHostConfigurationError();
  }
}

/** @internal */
export class AdminBffHostConfigurationError extends Error {
  constructor() {
    super('Admin BFF host configuration is invalid.');
    this.name = 'AdminBffHostConfigurationError';
  }
}

function parseAuthenticationMode(value: string | undefined): 'local' | 'oidc' {
  if (value === undefined) {
    return 'local';
  }

  if (value === 'local' || value === 'oidc') {
    return value;
  }

  throw new Error('Invalid authentication mode.');
}

function parseHost(value: string | undefined): string {
  const host = value ?? '127.0.0.1';

  if (!host.length || host.trim() !== host || /\s/u.test(host)) {
    throw new Error('Invalid HTTP host.');
  }

  return host;
}

function parsePort(value: string | undefined): number {
  const candidate = value ?? '3001';

  if (!/^\d+$/u.test(candidate)) {
    throw new Error('Invalid HTTP port.');
  }

  const port = Number(candidate);

  if (!Number.isSafeInteger(port) || port < 0 || port > 65_535) {
    throw new Error('Invalid HTTP port.');
  }

  return port;
}

function parseConfigDirectory(value: string | undefined): string {
  if (value === undefined) {
    return './config';
  }

  if (typeof value !== 'string' || !value.length || value.trim() !== value) {
    throw new Error('Missing configuration directory.');
  }

  return value;
}

function parseEnvironment(value: string | undefined): string {
  const environment = value ?? 'production';

  if (!environment.length || environment.trim() !== environment) {
    throw new Error('Invalid runtime environment.');
  }

  return environment;
}

function parseLocalAuthenticationConfig(
  environment: NodeJS.ProcessEnv,
  http: IAdminBffHostConfiguration['http'],
): LocalAuthenticationConfigType {
  const origin = parseOrigin(
    environment.ADMIN_BFF_PUBLIC_ORIGIN ?? `http://${http.host}:${http.port}`,
  );
  const loopbackHttp =
    origin.protocol === 'http:' && isLoopbackHostname(origin.hostname);
  const secureCookies = parseBoolean(
    environment.ADMIN_BFF_LOCAL_AUTH_SECURE_COOKIES,
    !loopbackHttp,
  );

  if (!loopbackHttp && (origin.protocol !== 'https:' || !secureCookies)) {
    throw new Error(
      'Public local authentication requires HTTPS secure cookies.',
    );
  }

  return Object.freeze({
    origin: origin.origin,
    secureCookies,
    ...(environment.ADMIN_BFF_LOCAL_AUTH_SESSION_IDLE_TTL_MS !== undefined && {
      sessionIdleTtlMs: parsePositiveInteger(
        environment.ADMIN_BFF_LOCAL_AUTH_SESSION_IDLE_TTL_MS,
      ),
    }),
    ...(environment.ADMIN_BFF_LOCAL_AUTH_SESSION_ABSOLUTE_TTL_MS !==
      undefined && {
      sessionAbsoluteTtlMs: parsePositiveInteger(
        environment.ADMIN_BFF_LOCAL_AUTH_SESSION_ABSOLUTE_TTL_MS,
      ),
    }),
    ...(environment.ADMIN_BFF_LOCAL_AUTH_MINIMUM_PASSWORD_LENGTH !==
      undefined && {
      minimumPasswordLength: parsePositiveInteger(
        environment.ADMIN_BFF_LOCAL_AUTH_MINIMUM_PASSWORD_LENGTH,
      ),
    }),
  });
}

function parseOrigin(value: string): URL {
  const origin = new URL(value);

  if (
    (origin.protocol !== 'http:' && origin.protocol !== 'https:') ||
    origin.origin !== value
  ) {
    throw new Error('Invalid public origin.');
  }

  return origin;
}

function isLoopbackHostname(hostname: string): boolean {
  return (
    hostname === 'localhost' ||
    hostname === '127.0.0.1' ||
    hostname === '::1' ||
    hostname === '[::1]'
  );
}

function parseBoolean(value: string | undefined, fallback: boolean): boolean {
  if (value === undefined) {
    return fallback;
  }

  if (value === 'true') {
    return true;
  }

  if (value === 'false') {
    return false;
  }

  throw new Error('Invalid boolean value.');
}

function parsePositiveInteger(value: string): number {
  if (!/^[1-9][0-9]*$/u.test(value)) {
    throw new Error('Invalid positive integer.');
  }

  const parsed = Number(value);

  if (!Number.isSafeInteger(parsed)) {
    throw new Error('Invalid positive integer.');
  }

  return parsed;
}
