import type { IAdminBffRouteContext } from '../admin-bff.interfaces.js';
import type { IAdminDiagnosticsRequestContext } from '@/diagnostics/index.js';
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
 * Diagnostics route handler.
 *
 * Returns detailed diagnostics about plugin discovery including
 * accepted and rejected plugins with structured reason taxonomy
 * and correlation metadata for incident triage.
 *
 * Observability: logs diagnostics generation timing and payload summary.
 */
export class AdminDiagnosticsRouteHandler implements IPlatformHttpRouteHandler<IAdminBffRouteContext> {
  readonly route = ADMIN_BFF_ROUTES.DIAGNOSTICS;
  readonly method = 'GET' as const;

  async handle(
    request: IPlatformHttpRequest,
    context: IAdminBffRouteContext,
  ): Promise<IPlatformHttpResponse> {
    const startTime = Date.now();

    context.logger.debug('Diagnostics generation started', {
      phase: AdminBffPhase.DIAGNOSTICS,
      correlationId: context.correlationId,
    });

    const result = await context.discoveryService.discover(context.identity);

    const requestContext: IAdminDiagnosticsRequestContext = {
      correlationId: context.correlationId,
      identity: context.identity,
      requestPath: request.path,
      userAgent: request.headers['user-agent']?.[0],
      clientIp: request.headers['x-forwarded-for']?.[0],
    };

    const diagnosticsPayload =
      context.diagnosticsService.generateDiagnosticsPayload(
        result,
        requestContext,
      );

    const duration = Date.now() - startTime;

    context.logger.info('Diagnostics generated', {
      phase: AdminBffPhase.DIAGNOSTICS,
      correlationId: context.correlationId,
      event: AdminBffLogEvents.DIAGNOSTICS_GENERATED,
      totalPlugins: diagnosticsPayload.plugins.length,
      acceptedCount: diagnosticsPayload.summary.acceptedCount,
      rejectedCount: diagnosticsPayload.summary.rejectedCount,
      duration,
    });

    return new PlatformHttpResponse({
      status: 200,
      body: {
        variant: 'json',
        data: diagnosticsPayload,
      },
    });
  }
}
