import type {
  IPlatformAuthenticationProvider,
  IPlatformModule,
  IPlatformModuleContext,
  IPlatformModuleManifest,
} from '@prosto/platform-sdk';
import {
  createPlatformLocalAuthRuntime,
  type IPlatformLocalAuthLogger,
  type IPlatformLocalAuthRuntime,
  PlatformArgon2idPasswordHasher,
} from '@prosto/platform-adapter-auth-local';
import {
  createTypeOrmPersistenceDescriptor,
  TYPEORM_DATA_SOURCE_SERVICE_TOKEN,
} from '@prosto/platform-adapter-typeorm';
import pkg from '../package.json' with { type: 'json' };
import type {
  IPlatformLocalAuthBootstrapOutput,
  IPlatformLocalAuthSessionModuleConfig,
  IPlatformLocalAuthSessionModuleFacade,
} from '@/interfaces/index.js';
import {
  LocalAuthAccountEntity,
  LocalAuthFailedLoginEntity,
  LocalAuthSessionEntity,
} from '@/entities/index.js';
import { auth_local_session_create_tables1710000000001 } from '@/migrations/index.js';
import {
  CryptoLocalAuthRandomness,
  DeferredResolver,
  DeferredRouteRegistration,
  LocalAuthBootstrapService,
  SystemLocalAuthClock,
  TypeOrmLocalAuthFailedLoginLimiter,
} from '@/services/index.js';
import { TypeOrmLocalAuthSessionStore } from '@/stores/index.js';

import 'reflect-metadata';

/** @alpha */
export const PLATFORM_AUTH_LOCAL_SESSION_MODULE_MANIFEST: IPlatformModuleManifest =
  {
    id: 'auth-local-session',
    version: pkg.version,
    sdkVersion: '^0.0.0',
    title: 'Local authentication session',
    description: 'Durable same-origin local username/password authentication.',
    dependencies: [],
    groups: ['PROSTO'],
  };

/**
 * @alpha
 * Owns TypeORM persistence and bootstrap lifecycle for local authentication.
 */
export class PlatformAuthLocalSessionModule implements IPlatformModule {
  readonly facade: IPlatformLocalAuthSessionModuleFacade;

  private _runtime?: IPlatformLocalAuthRuntime;

  constructor(private readonly _config: IPlatformLocalAuthSessionModuleConfig) {
    const resolver = new DeferredResolver(() => this._runtime?.resolver);
    const routes = Object.freeze([
      new DeferredRouteRegistration('GET', '/admin/api/v1/auth/session', () =>
        this._runtime?.routes.find(
          (item) => item.route === '/admin/api/v1/auth/session',
        ),
      ),
      new DeferredRouteRegistration('POST', '/admin/api/v1/auth/login', () =>
        this._runtime?.routes.find(
          (item) => item.route === '/admin/api/v1/auth/login',
        ),
      ),
      new DeferredRouteRegistration(
        'POST',
        '/admin/api/v1/auth/change-password',
        () =>
          this._runtime?.routes.find(
            (item) => item.route === '/admin/api/v1/auth/change-password',
          ),
      ),
      new DeferredRouteRegistration('POST', '/admin/api/v1/auth/logout', () =>
        this._runtime?.routes.find(
          (item) => item.route === '/admin/api/v1/auth/logout',
        ),
      ),
    ]);
    const provider: IPlatformAuthenticationProvider = Object.freeze({
      mode: 'local',
      resolver,
      publicRouteRegistrations: routes,
    });
    const getRuntime = () => this._runtime;

    this.facade = Object.freeze({
      provider,
      routes,
      get api(): IPlatformLocalAuthRuntime {
        const runtime = getRuntime();

        if (runtime === undefined) {
          throw new Error('Local authentication session module is not ready.');
        }

        return runtime;
      },
      get ready(): boolean {
        return resolver.ready;
      },
    });
  }

  init(context: IPlatformModuleContext): void {
    context.persistence?.descriptors?.register(context.moduleId, {
      owner: 'module',
      ownerId: context.moduleId,
      payload: createTypeOrmPersistenceDescriptor({
        entities: [
          LocalAuthAccountEntity,
          LocalAuthSessionEntity,
          LocalAuthFailedLoginEntity,
        ],
        migrations: [auth_local_session_create_tables1710000000001],
      }),
    });
  }

  async start(context: IPlatformModuleContext): Promise<void> {
    const dataSource = context.services.resolveRequired(
      TYPEORM_DATA_SOURCE_SERVICE_TOKEN,
    );
    const passwordHasher = new PlatformArgon2idPasswordHasher();
    const localAuthBootstrap = new LocalAuthBootstrapService(
      dataSource,
      passwordHasher,
      this._config.bootstrapRoles,
      this._config.bootstrapPermissions,
    );
    const output =
      this._config.bootstrapOutput ?? this._defaultBootstrapOutput();

    if (await localAuthBootstrap.requiresBootstrap()) {
      if (!output.isInteractive) {
        throw new Error(
          'Local authentication is uninitialized. Run prosto-platform auth bootstrap-local from an interactive TTY.',
        );
      }

      const result = await localAuthBootstrap.bootstrap();

      if (
        result.created &&
        result.username !== undefined &&
        result.password !== undefined
      ) {
        output.write(
          `\nLocal authentication bootstrap\nUsername: ${result.username}\nOne-time password: ${result.password}\nChange this password before using the admin BFF.\n`,
        );
      }
    }

    const store = new TypeOrmLocalAuthSessionStore(dataSource);
    this._runtime = createPlatformLocalAuthRuntime(this._config, {
      store,
      passwordHasher,
      limiter: new TypeOrmLocalAuthFailedLoginLimiter(dataSource),
      clock: new SystemLocalAuthClock(),
      randomness: new CryptoLocalAuthRandomness(),
      logger: this._logger(context),
    });
  }

  stop(_context: IPlatformModuleContext): void {
    this._runtime = undefined;
  }

  private _defaultBootstrapOutput(): IPlatformLocalAuthBootstrapOutput {
    return {
      isInteractive: process.stdout.isTTY,
      write: (message: string): void => {
        process.stdout.write(message);
      },
    };
  }

  private _logger(context: IPlatformModuleContext): IPlatformLocalAuthLogger {
    return {
      log: (event): void =>
        context.logger.info('Local authentication event.', {
          event: event.event,
          outcome: event.outcome,
          durationMs: event.durationMs,
          correlationId: event.correlationId,
        }),
    };
  }
}
