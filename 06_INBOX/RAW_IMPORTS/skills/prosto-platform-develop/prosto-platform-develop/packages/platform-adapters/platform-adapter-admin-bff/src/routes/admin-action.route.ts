import type { IAdminBffRouteContext } from '../admin-bff.interfaces.js';
import type {
  IPlatformHttpRequest,
  IPlatformHttpResponse,
  IPlatformHttpRouteHandler,
} from '@prosto/platform-sdk';
import { PlatformHttpResponse } from '@prosto/platform-sdk';
import {
  AdminBffErrorCodes,
  AdminBffLogEvents,
  AdminBffPhase,
} from '@/observability/index.js';
import { ADMIN_BFF_ROUTES } from '../admin-bff.constants.js';

/**
 * @alpha
 * Permission-aware action route handler.
 *
 * Evaluates operator permissions against the requested action gate
 * and returns an allow/deny decision with remediation metadata.
 *
 * Observability: logs action evaluation outcomes and permission denials.
 */
export class AdminActionRouteHandler implements IPlatformHttpRouteHandler<IAdminBffRouteContext> {
  readonly route = ADMIN_BFF_ROUTES.ACTION;
  readonly method = 'POST' as const;

  async handle(
    request: IPlatformHttpRequest,
    context: IAdminBffRouteContext,
  ): Promise<IPlatformHttpResponse> {
    const actionId = request.params['actionId'];

    if (!actionId) {
      context.logger.warn('Action evaluation requested without actionId', {
        phase: AdminBffPhase.ACTION_EVALUATION,
        correlationId: context.correlationId,
        errorCode: AdminBffErrorCodes.VALIDATION_FAILED,
      });

      return new PlatformHttpResponse({
        status: 400,
        body: {
          variant: 'json',
          data: {
            correlationId: context.correlationId,
            error: {
              code: 'MISSING_ACTION_ID',
              message: 'Action ID is required.',
            },
          },
        },
      });
    }

    context.logger.debug('Evaluating action gate', {
      phase: AdminBffPhase.ACTION_EVALUATION,
      correlationId: context.correlationId,
      actionId,
    });

    const evaluation = context.permissionService.evaluateAction(
      actionId,
      context.identity,
    );

    if (!evaluation.allowed) {
      context.logger.warn('Action denied', {
        phase: AdminBffPhase.ACTION_EVALUATION,
        correlationId: context.correlationId,
        event: AdminBffLogEvents.ACTION_EVALUATED,
        actionId,
        allowed: false,
        reasonCode: evaluation.reasonCode,
        errorCode: AdminBffErrorCodes.PERMISSION_DENIED,
      });

      return new PlatformHttpResponse({
        status: 403,
        body: {
          variant: 'json',
          data: {
            correlationId: context.correlationId,
            error: {
              code: evaluation.reasonCode ?? 'ACTION_DENIED',
              message: `Action "${actionId}" is not permitted.`,
              remediationHint: evaluation.remediationHint,
            },
          },
        },
      });
    }

    context.logger.info('Action allowed', {
      phase: AdminBffPhase.ACTION_EVALUATION,
      correlationId: context.correlationId,
      event: AdminBffLogEvents.ACTION_EVALUATED,
      actionId,
      allowed: true,
    });

    return new PlatformHttpResponse({
      status: 200,
      body: {
        variant: 'json',
        data: {
          correlationId: context.correlationId,
          data: {
            actionId: evaluation.actionId,
            allowed: true,
          },
        },
      },
    });
  }
}
