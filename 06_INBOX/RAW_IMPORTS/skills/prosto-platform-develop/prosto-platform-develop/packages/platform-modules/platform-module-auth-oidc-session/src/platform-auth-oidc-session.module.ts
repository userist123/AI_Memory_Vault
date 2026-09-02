import type {
  IPlatformModule,
  IPlatformModuleContext,
  IPlatformModuleManifest,
} from '@prosto/platform-sdk';
import pkg from '../package.json' with { type: 'json' };
import {
  createPlatformOidcSessionRuntime,
  type IPlatformOidcSessionLogger,
  type IPlatformOidcSessionRuntime,
} from '@prosto/platform-adapter-auth-oidc-session';
import {
  createTypeOrmPersistenceDescriptor,
  TYPEORM_DATA_SOURCE_SERVICE_TOKEN,
} from '@prosto/platform-adapter-typeorm';
import type {
  IPlatformAuthOidcSessionModuleConfig,
  IPlatformAuthOidcSessionModuleFacade,
} from '@/interfaces/index.js';
import { CLEANUP_BATCH_SIZE, CLEANUP_INTERVAL_MS } from '@/constants/index.js';
import { OidcSessionEntity, OidcTransactionEntity } from '@/entities/index.js';
import { auth_oidc_session_create_tables1710000000000 } from '@/migrations/index.js';
import {
  DeferredResolver,
  DeferredRouteRegistration,
} from '@/services/index.js';
import { TypeOrmOidcSessionStore } from '@/stores/index.js';

import 'reflect-metadata';

/** @alpha */
export const PLATFORM_AUTH_SESSION_MODULE_MANIFEST: IPlatformModuleManifest = {
  id: 'auth-session',
  version: pkg.version,
  sdkVersion: '^0.0.0',
  title: 'OIDC browser session',
  description: 'Durable same-origin OIDC browser session persistence.',
  dependencies: [],
  groups: ['PROSTO'],
};

/**
 * @alpha
 * Owns the TypeORM persistence lifecycle for the framework-neutral OIDC session runtime.
 */
export class PlatformAuthOidcSessionModule implements IPlatformModule {
  readonly facade: IPlatformAuthOidcSessionModuleFacade;

  private _runtime?: IPlatformOidcSessionRuntime;
  private _cleanupTimer?: NodeJS.Timeout;
  private _logger?: IPlatformModuleContext['logger'];

  constructor(private readonly _config: IPlatformAuthOidcSessionModuleConfig) {
    const resolver = new DeferredResolver(() => this._runtime?.resolver);
    const routes = Object.freeze([
      new DeferredRouteRegistration('GET', '/auth/login', () =>
        this._runtime?.routes.find((item) => item.route === '/auth/login'),
      ),
      new DeferredRouteRegistration('GET', '/auth/callback', () =>
        this._runtime?.routes.find((item) => item.route === '/auth/callback'),
      ),
      new DeferredRouteRegistration('POST', '/auth/logout', () =>
        this._runtime?.routes.find((item) => item.route === '/auth/logout'),
      ),
    ]);

    this.facade = Object.freeze({
      resolver,
      routes,
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
        entities: [OidcSessionEntity, OidcTransactionEntity],
        migrations: [auth_oidc_session_create_tables1710000000000],
      }),
    });
  }

  start(context: IPlatformModuleContext): void {
    const dataSource = context.services.resolveRequired(
      TYPEORM_DATA_SOURCE_SERVICE_TOKEN,
    );
    const store = new TypeOrmOidcSessionStore(dataSource);

    this._logger = context.logger;
    this._runtime = createPlatformOidcSessionRuntime(this._config, {
      store,
      cipher: this._config.cipher,
      accessTokenResolver: this._config.accessTokenResolver,
      logger: this._sessionLogger(context),
    });
    this._cleanupTimer = setInterval(() => {
      void this._cleanup(store);
    }, CLEANUP_INTERVAL_MS);
    this._cleanupTimer.unref();
  }

  stop(_context: IPlatformModuleContext): void {
    if (this._cleanupTimer !== undefined) {
      clearInterval(this._cleanupTimer);
    }

    this._cleanupTimer = undefined;
    this._runtime = undefined;
    this._logger = undefined;
  }

  private _sessionLogger(
    context: IPlatformModuleContext,
  ): IPlatformOidcSessionLogger {
    return {
      log: (event): void =>
        context.logger.info('OIDC session event.', {
          event: event.event,
          outcome: event.outcome,
          durationMs: event.durationMs,
          correlationId: event.correlationId,
        }),
    };
  }

  private async _cleanup(store: TypeOrmOidcSessionStore): Promise<void> {
    try {
      await store.cleanupExpired(Date.now(), CLEANUP_BATCH_SIZE);
    } catch {
      this._logger?.warn('OIDC session expiry cleanup was unavailable.', {
        event: 'session_cleanup',
        outcome: 'unavailable',
      });
    }
  }
}
