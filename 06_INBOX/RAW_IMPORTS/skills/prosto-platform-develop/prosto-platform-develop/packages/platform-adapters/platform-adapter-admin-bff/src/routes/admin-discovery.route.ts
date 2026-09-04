import type { IAdminBffRouteContext } from '../admin-bff.interfaces.js';
import type {
  IPlatformHttpRequest,
  IPlatformHttpResponse,
  IPlatformHttpRouteHandler,
} from '@prosto/platform-sdk';
import { PlatformHttpResponse } from '@prosto/platform-sdk';
import { AdminBffLogEvents, AdminBffPhase } from '@/observability/index.js';
import { ADMIN_BFF_ROUTES } from '../admin-bff.constants.js';

/**
 * @alpha
 * Plugin discovery route handler.
 *
 * Aggregates plugin manifests, validates them against admin contracts,
 * and returns a policy-aware discovery payload for the admin shell.
 *
 * Observability: logs discovery pipeline timing, accepted/rejected counts,
 * and per-plugin outcomes for operational analysis.
 */
export class AdminDiscoveryRouteHandler implements IPlatformHttpRouteHandler<IAdminBffRouteContext> {
  readonly route = ADMIN_BFF_ROUTES.DISCOVERY;
  readonly method = 'GET' as const;

  async handle(
    _request: IPlatformHttpRequest,
    context: IAdminBffRouteContext,
  ): Promise<IPlatformHttpResponse> {
    const startTime = Date.now();

    context.logger.info('Discovery pipeline started', {
      phase: AdminBffPhase.DISCOVERY,
      correlationId: context.correlationId,
      event: AdminBffLogEvents.DISCOVERY_STARTED,
    });

    try {
      const result = await context.discoveryService.discover(context.identity);

      const duration = Date.now() - startTime;

      context.logger.info('Discovery pipeline completed', {
        phase: AdminBffPhase.DISCOVERY,
        correlationId: context.correlationId,
        event: AdminBffLogEvents.DISCOVERY_COMPLETED,
        acceptedCount: result.diagnostics.acceptedCount,
        rejectedCount: result.diagnostics.rejectedCount,
        duration,
        totalPlugins:
          result.diagnostics.acceptedCount + result.diagnostics.rejectedCount,
      });

      for (const plugin of result.payload.plugins) {
        context.logger.debug('Plugin accepted', {
          phase: AdminBffPhase.DISCOVERY,
          correlationId: context.correlationId,
          event: AdminBffLogEvents.PLUGIN_ACCEPTED,
          pluginId: plugin.id,
          pluginVersion: plugin.version,
          trustClass: plugin.trustClass,
        });
      }

      for (const rejected of result.payload.rejected) {
        context.logger.debug('Plugin rejected', {
          phase: AdminBffPhase.DISCOVERY,
          correlationId: context.correlationId,
          event: AdminBffLogEvents.PLUGIN_REJECTED,
          pluginId: rejected.id ?? 'unknown',
          pluginVersion: rejected.version,
          reasonCode: rejected.reasonCode,
        });
      }

      return new PlatformHttpResponse({
        status: 200,
        body: {
          variant: 'json',
          data: {
            correlationId: context.correlationId,
            data: result.payload,
            diagnostics: {
              ...result.diagnostics,
              duration,
            },
          },
        },
      });
    } catch (error) {
      const duration = Date.now() - startTime;

      context.logger.error('Discovery pipeline failed', {
        phase: AdminBffPhase.DISCOVERY,
        correlationId: context.correlationId,
        event: AdminBffLogEvents.DISCOVERY_FAILED,
        errorCode: 'ADMIN_BFF_DISCOVERY_FAILED',
        duration,
        error: error instanceof Error ? error.message : String(error),
      });

      throw error;
    }
  }
}
