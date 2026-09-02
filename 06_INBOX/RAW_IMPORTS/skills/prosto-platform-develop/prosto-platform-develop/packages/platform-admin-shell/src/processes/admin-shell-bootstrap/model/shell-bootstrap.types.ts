import type { AdminDiscoveryClient } from '@/shared/api/admin-discovery';
import type { PluginStoreType } from '@/entities/plugin';
import type { DiagnosticsStoreType } from '@/entities/diagnostics';
import type { PluginRuntimeService } from '@/features/plugin-runtime';
import type {
  AdminShellTelemetryService,
  IAdminShellLogger,
} from '@/shared/observability';

export interface IShellBootstrapOptions {
  readonly discoveryClient: AdminDiscoveryClient;
  readonly pluginRuntime: PluginRuntimeService;
  readonly pluginStore: PluginStoreType;
  readonly diagnosticsStore: DiagnosticsStoreType;
  readonly navigateToLogin: () => void | Promise<void>;
  readonly telemetry?: AdminShellTelemetryService;
  readonly logger?: IAdminShellLogger;
}

export interface IShellBootstrapResult {
  readonly success: boolean;
  readonly degraded: boolean;
  readonly loadedCount: number;
  readonly rejectedCount: number;
  readonly message: string;
}

export interface IDiscoveryFailureContext {
  readonly reasonCode: import('@/entities/diagnostics').DegradedModeReasonType;
  readonly errorCode: string;
  readonly statusCode?: number;
  readonly message: string;
}
