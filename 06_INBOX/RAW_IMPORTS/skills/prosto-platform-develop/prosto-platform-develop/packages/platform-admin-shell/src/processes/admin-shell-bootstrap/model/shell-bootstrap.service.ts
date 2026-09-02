import type {
  IDiscoveryFailureContext,
  IShellBootstrapOptions,
  IShellBootstrapResult,
} from './shell-bootstrap.types';
import { AdminShellErrorCodes } from '@/shared/observability';

function resolveDiscoveryFailure(
  reason: string,
  statusCode?: number,
): IDiscoveryFailureContext {
  switch (reason) {
    case 'NETWORK_ERROR':
      return {
        reasonCode: 'DISCOVERY_NETWORK_ERROR',
        errorCode: AdminShellErrorCodes.DISCOVERY_NETWORK_ERROR,
        message: 'Cannot reach admin BFF. Shell running with no plugins.',
      };

    case 'TIMEOUT':
      return {
        reasonCode: 'DISCOVERY_TIMEOUT',
        errorCode: AdminShellErrorCodes.DISCOVERY_TIMEOUT,
        message:
          'Admin BFF discovery request timed out. Shell running with no plugins.',
      };

    case 'HTTP_ERROR':
      return {
        reasonCode: 'DISCOVERY_HTTP_ERROR',
        errorCode: AdminShellErrorCodes.DISCOVERY_HTTP_ERROR,
        message: `Admin BFF returned HTTP ${statusCode ?? 'unknown'}. Shell running with no plugins.`,
        statusCode,
      };

    case 'VALIDATION_FAILED':
      return {
        reasonCode: 'DISCOVERY_VALIDATION_FAILED',
        errorCode: AdminShellErrorCodes.DISCOVERY_VALIDATION_FAILED,
        message:
          'Discovery payload validation failed. Shell running with no plugins.',
      };

    default:
      return {
        reasonCode: 'UNKNOWN',
        errorCode: AdminShellErrorCodes.DISCOVERY_NETWORK_ERROR,
        message: 'Unknown discovery error. Shell running with no plugins.',
      };
  }
}

export async function shellBootstrap(
  options: IShellBootstrapOptions,
): Promise<IShellBootstrapResult> {
  const { discoveryClient, pluginRuntime, diagnosticsStore, telemetry } =
    options;

  telemetry?.recordStartupStarted();
  telemetry?.recordDiscoveryStarted();

  const startTime = performance.now();
  const discoveryStartTime = performance.now();
  const result = await discoveryClient.getDiscovery();
  const discoveryDurationMs = performance.now() - discoveryStartTime;

  if (!result.success) {
    const reason = result.reason;

    if (reason === 'UNAUTHENTICATED') {
      telemetry?.recordDiscoveryFailed(reason, discoveryDurationMs);
      telemetry?.recordStartupFailed(reason);

      await options.navigateToLogin();

      return {
        success: false,
        degraded: false,
        loadedCount: 0,
        rejectedCount: 0,
        message: 'Authentication is required.',
      };
    }

    const statusCode =
      reason === 'HTTP_ERROR'
        ? (result as { readonly statusCode?: number }).statusCode
        : undefined;
    const context = resolveDiscoveryFailure(reason, statusCode);

    diagnosticsStore.enterDegradedMode(context.reasonCode, context.message);

    telemetry?.recordDiscoveryFailed(
      reason,
      discoveryDurationMs,
      context.errorCode,
    );
    telemetry?.recordStartupFailed(reason);

    return {
      success: false,
      degraded: true,
      loadedCount: 0,
      rejectedCount: 0,
      message: context.message,
    };
  }

  telemetry?.recordDiscoveryCompleted(
    discoveryDurationMs,
    result.payload.plugins.length,
    result.correlationId,
  );

  const pluginDescriptors = result.payload.plugins;
  const pluginRuntimeResult =
    await pluginRuntime.bootstrapPlugins(pluginDescriptors);

  const { loadedCount, rejectedCount } = pluginRuntimeResult;
  const totalDurationMs = performance.now() - startTime;

  if (rejectedCount > 0) {
    diagnosticsStore.enterDegradedMode(
      'PLUGIN_LOAD_FAILURE',
      `${rejectedCount} plugin(s) failed to load. Shell is running in degraded mode.`,
    );

    telemetry?.recordStartupDegraded(totalDurationMs);
  } else {
    telemetry?.recordStartupCompleted(totalDurationMs);
  }

  return {
    success: true,
    degraded: rejectedCount > 0,
    loadedCount,
    rejectedCount,
    message: `Shell loaded ${loadedCount} plugin(s).`,
  };
}
