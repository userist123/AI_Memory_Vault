import type { IAdminBffLogger } from '@/observability/index.js';
import {
  AdminBffErrorCodes,
  AdminBffPhase,
  ConsoleAdminBffLogger,
} from '@/observability/index.js';
import type { IAdminDiagnosticsService } from '@/diagnostics/index.js';
import type {
  IAdminBffRouteContext,
  IAdminDiscoveryAggregationService,
  IAdminPermissionMappingService,
} from './admin-bff.interfaces.js';
import {
  AdminActionRouteHandler,
  AdminDiagnosticsRouteHandler,
  AdminDiscoveryRouteHandler,
  AdminHealthRouteHandler,
} from './routes/index.js';
import type {
  IPlatformHttpRequest,
  IPlatformHttpResponse,
  IPlatformHttpRouteHandler,
} from '@prosto/platform-sdk';
import {
  isPlatformDelegatedIdentity,
  PlatformHttpError,
  PlatformHttpResponse,
} from '@prosto/platform-sdk';

/**
 * @alpha
 * Configuration options for the admin BFF adapter.
 */
export interface IPlatformAdminBffAdapterConfig {
  readonly logger?: IAdminBffLogger;
}

/**
 * @alpha
 * Framework-agnostic admin BFF adapter.
 *
 * Provides policy-aware admin APIs, plugin discovery aggregation,
 * permission mapping, and diagnostics for the hybrid admin shell model.
 *
 * This adapter does not depend on any HTTP framework. Consumers
 * wire it to their transport layer via the route handler interface.
 *
 * Observability: every request is logged with correlation ID, phase timing,
 * and structured context fields per ADR-0007.
 */
export class PlatformAdminBffAdapter {
  private readonly _handlers: readonly IPlatformHttpRouteHandler<IAdminBffRouteContext>[];
  private readonly _logger: IAdminBffLogger;
  private readonly _startedAt: number;

  constructor(
    private readonly _discoveryService: IAdminDiscoveryAggregationService,
    private readonly _permissionService: IAdminPermissionMappingService,
    private readonly _diagnosticsService: IAdminDiagnosticsService,
    config?: IPlatformAdminBffAdapterConfig,
  ) {
    this._logger = config?.logger ?? new ConsoleAdminBffLogger();
    this._startedAt = Date.now();

    this._handlers = [
      new AdminDiscoveryRouteHandler(),
      new AdminActionRouteHandler(),
      new AdminHealthRouteHandler(),
      new AdminDiagnosticsRouteHandler(),
    ];

    this._logger.info('Admin BFF adapter initialized', {
      phase: 'init',
      handlerCount: this._handlers.length,
    });
  }

  /**
   * Returns all registered route handlers.
   */
  getHandlers(): readonly IPlatformHttpRouteHandler<IAdminBffRouteContext>[] {
    return this._handlers;
  }

  /**
   * Finds a route handler matching the given method and path.
   */
  findHandler(
    method: string,
    path: string,
  ): IPlatformHttpRouteHandler<IAdminBffRouteContext> | undefined {
    return this._handlers.find(
      (handler) =>
        handler.method === method && this._matchRoute(handler.route, path),
    );
  }

  /**
   * Dispatches a request to the matching route handler.
   *
   * @param request — SDK-normalized HTTP request with delegated identity.
   * @param signal — optional abort signal forwarded to the context.
   * @returns SDK-normalized HTTP response.
   * @throws PlatformHttpError when identity is not delegated.
   */
  async handleRequest(
    request: IPlatformHttpRequest,
    signal?: AbortSignal,
  ): Promise<IPlatformHttpResponse> {
    const correlationId = request.correlationId;
    const startTime = Date.now();

    if (!isPlatformDelegatedIdentity(request.identity)) {
      throw new PlatformHttpError(
        'HTTP_UNAUTHENTICATED',
        'Anonymous identity is not allowed for admin BFF routes.',
        { correlationId },
      );
    }

    this._logger.info('Request received', {
      phase: AdminBffPhase.REQUEST,
      correlationId,
      method: request.method,
      path: request.path,
    });

    const handler = this.findHandler(request.method, request.path);

    if (!handler) {
      this._logger.warn('Route not found', {
        phase: AdminBffPhase.ROUTE_MATCH,
        correlationId,
        method: request.method,
        path: request.path,
        errorCode: AdminBffErrorCodes.ROUTE_NOT_FOUND,
      });

      return new PlatformHttpResponse({
        status: 404,
        body: {
          variant: 'json',
          data: {
            correlationId,
            error: {
              code: 'ROUTE_NOT_FOUND',
              message: `No handler found for ${request.method} ${request.path}.`,
            },
          },
        },
      });
    }

    this._logger.debug('Route matched', {
      phase: AdminBffPhase.ROUTE_MATCH,
      correlationId,
      handlerRoute: handler.route,
      handlerMethod: handler.method,
    });

    const context: IAdminBffRouteContext = {
      correlationId,
      identity: request.identity,
      signal: signal ?? new AbortController().signal,
      discoveryService: this._discoveryService,
      permissionService: this._permissionService,
      diagnosticsService: this._diagnosticsService,
      logger: this._logger,
    };

    this._logger.debug('Handler dispatch started', {
      phase: AdminBffPhase.REQUEST,
      correlationId,
      route: handler.route,
      method: handler.method,
    });

    const response = await handler.handle(request, context);

    const duration = Date.now() - startTime;

    if (response.status >= 400) {
      this._logger.warn('Request completed with error status', {
        phase: AdminBffPhase.REQUEST,
        correlationId,
        method: request.method,
        path: request.path,
        status: response.status,
        duration,
      });
    } else {
      this._logger.info('Request completed', {
        phase: AdminBffPhase.REQUEST,
        correlationId,
        method: request.method,
        path: request.path,
        status: response.status,
        duration,
      });
    }

    return response;
  }

  /**
   * Returns adapter uptime in milliseconds.
   */
  getUptime(): number {
    return Date.now() - this._startedAt;
  }

  protected _matchRoute(pattern: string, path: string): boolean {
    const patternParts = pattern.split('/');
    const pathParts = path.split('/');

    if (patternParts.length !== pathParts.length) {
      return false;
    }

    return patternParts.every((part, index) => {
      if (part.startsWith(':')) {
        return true;
      }
      return part === pathParts[index];
    });
  }
}
