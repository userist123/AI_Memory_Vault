import {
  AdminDiagnosticsService,
  AdminDiscoveryAggregationService,
  AdminPermissionMappingService,
  PlatformAdminBffAdapter,
  type IAdminBffLogger,
  type IAdminBffRouteContext,
  type IAdminDiagnosticsService,
  type IAdminDiscoveryAggregationService,
  type IAdminPluginCatalogSource,
  type IAdminPermissionMappingService,
} from '@prosto/platform-adapter-admin-bff';
import {
  PlatformHttpServer,
  type IPlatformHttpServerConfig,
} from '@prosto/platform-adapter-http';
import {
  ADMIN_AUTHENTICATION_API_SCHEMA_VERSION,
  ADMIN_COMPATIBILITY_CONTRACT_VERSION,
  AdminPluginCompatibilityEvaluator,
  AdminUIPluginManifestValidator,
  type IAdminPermissionPolicy,
} from '@prosto/platform-admin-contracts';
import {
  RuntimeBuilder,
  type IPlatformRuntime,
  type IRuntimeBuilder,
  type IRuntimeBuilderOptions,
} from '@prosto/platform-core';
import {
  isPlatformDelegatedIdentity,
  PlatformHttpError,
  PlatformHttpResponse,
  PlatformHttpRouteRegistration,
  type IPlatformHttpRouteContext,
  type IPlatformHttpRouteContextFactory,
  type IPlatformHttpRouteContextFactoryInput,
  type IPlatformHttpRouteHandler,
  type IPlatformHttpResponse,
  type IPlatformHttpRouteRegistration,
  type IPlatformAuthenticationProvider,
} from '@prosto/platform-sdk';

/** Configuration for the BFF services constructed by this composition root. */
export interface IAdminBffHostConfig {
  readonly catalogSource: IAdminPluginCatalogSource;
  readonly permissionPolicy: IAdminPermissionPolicy;
  readonly shellVersion: string;
  readonly environment: string;
  readonly discoveryPipelineVersion: string;
  readonly logger: IAdminBffLogger;
}

/** Inputs owned by the runtime host, rather than either HTTP or BFF adapter. */
export interface IAdminBffRuntimeHostConfig {
  readonly http: Omit<IPlatformHttpServerConfig, 'identityResolver'>;
  /** Selected authentication facade, including identity resolver and public routes. */
  readonly authenticationProvider: IPlatformAuthenticationProvider;
  readonly runtime: IRuntimeBuilderOptions;
  readonly adminBff: IAdminBffHostConfig;
  /** SDK route registrations supplied by the composition root before startup. */
  readonly additionalRouteRegistrations?: readonly IPlatformHttpRouteRegistration[];
  readonly runtimeBuilder?: IRuntimeBuilder;
}

class BaseContextFactory implements IPlatformHttpRouteContextFactory<IPlatformHttpRouteContext> {
  async create(
    input: IPlatformHttpRouteContextFactoryInput,
  ): Promise<IPlatformHttpRouteContext> {
    return input.baseContext;
  }
}

/** Provides the provider-neutral OIDC session status consumed by the shell. */
class OidcAuthenticationSessionRoute implements IPlatformHttpRouteRegistration {
  readonly method = 'GET';
  readonly route = '/admin/api/v1/auth/session';

  async execute(
    input: IPlatformHttpRouteContextFactoryInput,
  ): Promise<IPlatformHttpResponse> {
    const authenticated = isPlatformDelegatedIdentity(
      input.baseContext.identity,
    );

    return new PlatformHttpResponse({
      status: 200,
      body: {
        variant: 'json',
        data: authenticated
          ? {
              mode: 'oidc',
              state: 'authenticated',
              schemaVersion: ADMIN_AUTHENTICATION_API_SCHEMA_VERSION,
            }
          : {
              mode: 'oidc',
              state: 'anonymous',
              loginUrl: '/auth/login',
              schemaVersion: ADMIN_AUTHENTICATION_API_SCHEMA_VERSION,
            },
      },
    });
  }
}

class StaticPlatformRouteHandler implements IPlatformHttpRouteHandler<IPlatformHttpRouteContext> {
  readonly method = 'GET';

  constructor(
    readonly route: string,
    private readonly _handle: () => Promise<IPlatformHttpResponse>,
  ) {}

  handle = async (): Promise<IPlatformHttpResponse> => this._handle();
}

/** Rejects anonymous requests before any Admin BFF handler or service runs. */
export class AdminBffRouteContextFactory implements IPlatformHttpRouteContextFactory<IAdminBffRouteContext> {
  constructor(
    private readonly _services: {
      readonly discoveryService: IAdminDiscoveryAggregationService;
      readonly permissionService: IAdminPermissionMappingService;
      readonly diagnosticsService: IAdminDiagnosticsService;
      readonly logger: IAdminBffLogger;
    },
  ) {}

  async create(
    input: IPlatformHttpRouteContextFactoryInput,
  ): Promise<IAdminBffRouteContext> {
    if (!isPlatformDelegatedIdentity(input.baseContext.identity)) {
      throw new PlatformHttpError(
        'HTTP_UNAUTHENTICATED',
        'A delegated identity is required for admin BFF routes.',
        { correlationId: input.baseContext.correlationId },
      );
    }

    return {
      correlationId: input.baseContext.correlationId,
      identity: input.baseContext.identity,
      signal: input.baseContext.signal,
      discoveryService: this._services.discoveryService,
      permissionService: this._services.permissionService,
      diagnosticsService: this._services.diagnosticsService,
      logger: this._services.logger,
    };
  }
}

/**
 * Executable composition root lifecycle. It owns the concrete RuntimeBuilder,
 * Admin BFF services, HTTP route bridge, and shutdown ordering.
 */
export class PlatformAdminBffRuntimeHost {
  readonly runtime: IPlatformRuntime;
  readonly httpServer: PlatformHttpServer;

  private _startPromise: Promise<void> | undefined;
  private _stopPromise: Promise<void> | undefined;

  constructor(config: IAdminBffRuntimeHostConfig) {
    this.runtime = (config.runtimeBuilder ?? new RuntimeBuilder()).build(
      config.runtime,
    );

    const permissionService = new AdminPermissionMappingService({
      policy: config.adminBff.permissionPolicy,
    });

    const discoveryService = new AdminDiscoveryAggregationService(
      config.adminBff.catalogSource,
      new AdminUIPluginManifestValidator(),
      new AdminPluginCompatibilityEvaluator(),
      {
        shellVersion: config.adminBff.shellVersion,
        supportedContractVersion: ADMIN_COMPATIBILITY_CONTRACT_VERSION,
      },
      { permissionService },
    );

    const diagnosticsService = new AdminDiagnosticsService({
      environment: config.adminBff.environment,
      shellVersion: config.adminBff.shellVersion,
      discoveryPipelineVersion: config.adminBff.discoveryPipelineVersion,
      enableDetailedLogging: false,
    });

    const adminBffAdapter = new PlatformAdminBffAdapter(
      discoveryService,
      permissionService,
      diagnosticsService,
      { logger: config.adminBff.logger },
    );

    const adminContextFactory = new AdminBffRouteContextFactory({
      discoveryService,
      permissionService,
      diagnosticsService,
      logger: config.adminBff.logger,
    });

    this.httpServer = new PlatformHttpServer({
      ...config.http,
      identityResolver: config.authenticationProvider.resolver,
    });
    this.httpServer.registerRoutes([
      ...config.authenticationProvider.publicRouteRegistrations,
      ...(config.authenticationProvider.mode === 'oidc'
        ? [new OidcAuthenticationSessionRoute()]
        : []),
      ...adminBffAdapter
        .getHandlers()
        .map(
          (handler) =>
            new PlatformHttpRouteRegistration(handler, adminContextFactory),
        ),
      this._createPlatformHealthRegistration(),
      this._createPlatformReadinessRegistration(),
      ...(config.additionalRouteRegistrations ?? []),
    ]);
  }

  static create(
    config: IAdminBffRuntimeHostConfig,
  ): PlatformAdminBffRuntimeHost {
    return new PlatformAdminBffRuntimeHost(config);
  }

  /** Starts core before exposing routes that report runtime state. */
  start(): Promise<void> {
    this._startPromise ??= this._start();
    return this._startPromise;
  }

  /** Stops the listener before stopping the platform runtime. */
  stop(): Promise<void> {
    this._stopPromise ??= this._stop();
    return this._stopPromise;
  }

  private async _start(): Promise<void> {
    await this.runtime.start();

    if (!this.runtime.started) {
      await this.runtime.stop();
      throw new Error('Platform runtime did not reach a started state.');
    }

    try {
      await this.httpServer.start();
    } catch (error) {
      await this.runtime.stop();
      throw error;
    }
  }

  private async _stop(): Promise<void> {
    if (this.httpServer.state === 'started') {
      await this.httpServer.stop();
    }

    if (!this.runtime.stopped) {
      await this.runtime.stop();
    }
  }

  private _createPlatformHealthRegistration(): IPlatformHttpRouteRegistration {
    return new PlatformHttpRouteRegistration(
      new StaticPlatformRouteHandler(
        '/platform/health',
        async () =>
          new PlatformHttpResponse({
            status: this.runtime.started && !this.runtime.stopped ? 200 : 503,
            body: {
              variant: 'json',
              data: {
                status: this.runtime.degraded ? 'degraded' : 'healthy',
                runtime: this.runtime.reports,
              },
            },
          }),
      ),
      new BaseContextFactory(),
    );
  }

  private _createPlatformReadinessRegistration(): IPlatformHttpRouteRegistration {
    return new PlatformHttpRouteRegistration(
      new StaticPlatformRouteHandler('/platform/ready', async () => {
        const ready =
          this.runtime.started &&
          !this.runtime.degraded &&
          !this.runtime.stopped;

        return new PlatformHttpResponse({
          status: ready ? 200 : 503,
          body: {
            variant: 'json',
            data: { status: ready ? 'ready' : 'not_ready' },
          },
        });
      }),
      new BaseContextFactory(),
    );
  }
}

/** Installs composition-root shutdown policy; adapters never manage signals. */
export function installShutdownHandlers(
  host: PlatformAdminBffRuntimeHost,
): void {
  let shutdown: Promise<void> | undefined;

  const stop = (): void => {
    shutdown ??= host.stop().then(
      (): void => process.exit(0),
      (): void => process.exit(1),
    );
  };

  process.once('SIGINT', stop);
  process.once('SIGTERM', stop);
}
