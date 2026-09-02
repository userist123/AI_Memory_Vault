import { resolve as resolvePath } from 'node:path';
import { ConsoleAdminBffLogger } from '@prosto/platform-adapter-admin-bff';
import { createPlatformAesKeyRingCipher } from '@prosto/platform-adapter-aes-key-ring';
import { PlatformOidcBearerResolver } from '@prosto/platform-adapter-auth-oidc';
import { createPlatformOidcAuthenticationProvider } from '@prosto/platform-adapter-auth-oidc-session';
import { TypeOrmPersistenceProvider } from '@prosto/platform-adapter-typeorm';
import {
  PLATFORM_AUTH_SESSION_MODULE_MANIFEST,
  PlatformAuthOidcSessionModule,
} from '@prosto/platform-module-auth-oidc-session';
import {
  PLATFORM_AUTH_LOCAL_SESSION_MODULE_MANIFEST,
  PlatformAuthLocalSessionModule,
} from '@prosto/platform-module-auth-local-session';
import type {
  IPlatformAuthenticationProvider,
  IPlatformModule,
  IPlatformModuleManifest,
} from '@prosto/platform-sdk';
import type {
  IAdminPermissionPolicy,
  IAdminUIPluginManifest,
} from '@prosto/platform-admin-contracts';
import { ADMIN_PERMISSION_POLICY_SCHEMA_VERSION } from '@prosto/platform-admin-contracts';
import {
  installShutdownHandlers,
  PlatformAdminBffRuntimeHost,
} from './admin-bff-http-host.js';
import { parseBearerAuthConfig } from './config/auth-config.js';
import { parseAdminBffHostConfiguration } from './config/host-config.js';
import { parseKeyRingConfig } from './config/key-ring-config.js';
import { parseSessionConfig } from './config/session-config.js';

const DEFAULT_PERMISSION_POLICY: IAdminPermissionPolicy = {
  schemaVersion: ADMIN_PERMISSION_POLICY_SCHEMA_VERSION,
  roleMappings: [{ roleId: 'admin', permissions: ['admin.access'] }],
  actionGates: [],
};

function readManifests(): readonly IAdminUIPluginManifest[] {
  const value = process.env.ADMIN_BFF_ADMIN_MANIFESTS_JSON;

  if (!value) {
    return [];
  }

  const parsed: unknown = JSON.parse(value);

  if (!Array.isArray(parsed)) {
    throw new Error('ADMIN_BFF_ADMIN_MANIFESTS_JSON must be a JSON array.');
  }

  return parsed as IAdminUIPluginManifest[];
}

async function main(): Promise<void> {
  const logger = new ConsoleAdminBffLogger();
  const configuration = parseAdminBffHostConfiguration(process.env);
  const host =
    configuration.auth.mode === 'local'
      ? await createLocalHost(configuration, configuration.auth.local, logger)
      : createOidcHost(configuration, logger);

  await host.start();
  installShutdownHandlers(host);
}

async function createLocalHost(
  configuration: ReturnType<typeof parseAdminBffHostConfiguration>,
  localAuth: NonNullable<
    Extract<
      ReturnType<typeof parseAdminBffHostConfiguration>['auth'],
      { readonly mode: 'local' }
    >['local']
  >,
  logger: ConsoleAdminBffLogger,
): Promise<PlatformAdminBffRuntimeHost> {
  // await mkdir(resolvePath('..', '..', '.prosto'), { recursive: true });
  const sessionModule = new PlatformAuthLocalSessionModule({
    ...localAuth,
    bootstrapRoles: ['admin'],
  });

  return createHost(
    configuration,
    logger,
    sessionModule.facade.provider,
    PLATFORM_AUTH_LOCAL_SESSION_MODULE_MANIFEST,
    sessionModule,
  );
}

function createOidcHost(
  configuration: ReturnType<typeof parseAdminBffHostConfiguration>,
  logger: ConsoleAdminBffLogger,
): PlatformAdminBffRuntimeHost {
  const bearerResolver = new PlatformOidcBearerResolver(
    parseBearerAuthConfig(process.env),
  );
  const cipher = createPlatformAesKeyRingCipher(
    parseKeyRingConfig(process.env),
  );
  const sessionModule = new PlatformAuthOidcSessionModule({
    ...parseSessionConfig(process.env),
    cipher,
    accessTokenResolver: bearerResolver,
  });

  return createHost(
    configuration,
    logger,
    createPlatformOidcAuthenticationProvider(
      sessionModule.facade,
      bearerResolver,
    ),
    PLATFORM_AUTH_SESSION_MODULE_MANIFEST,
    sessionModule,
  );
}

function createHost(
  configuration: ReturnType<typeof parseAdminBffHostConfiguration>,
  logger: ConsoleAdminBffLogger,
  authenticationProvider: IPlatformAuthenticationProvider,
  manifest: IPlatformModuleManifest,
  module: IPlatformModule,
): PlatformAdminBffRuntimeHost {
  const host = PlatformAdminBffRuntimeHost.create({
    http: {
      host: configuration.http.host,
      port: configuration.http.port,
    },
    authenticationProvider,
    runtime: {
      configDir: resolvePath(configuration.configDir),
      environment: configuration.environment,
      persistenceProvider: new TypeOrmPersistenceProvider(),
      modules: [
        {
          type: 'memory',
          manifest,
          module,
        },
      ],
    },
    adminBff: {
      catalogSource: {
        // TODO: Add a real catalog source.
        fetchUIPluginManifests: async () => readManifests(),
      },
      permissionPolicy: DEFAULT_PERMISSION_POLICY,
      shellVersion: process.env.ADMIN_BFF_ADMIN_SHELL_VERSION ?? '1.0.0',
      environment: configuration.environment,
      discoveryPipelineVersion: 'admin-bff-http-host.v1',
      logger,
    },
  });

  return host;
}

void main().catch((error) => {
  console.error(
    error instanceof Error ? error.message : 'Host startup failed.',
  );

  process.exit(1);
});
