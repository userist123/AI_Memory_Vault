import type { IAdminDiscoveredPluginDescriptor } from '@prosto/platform-admin-contracts';
import type { PluginStoreType } from '@/entities/plugin';
import type { DiagnosticsStoreType } from '@/entities/diagnostics';
import type { PermissionGuardService } from '@/features/permissions';
import type {
  AdminShellTelemetryService,
  IAdminShellLogger,
} from '@/shared/observability';

/**
 * @alpha
 * Dependencies required by PluginRuntimeService.
 */
export interface IPluginRuntimeDependencies {
  readonly pluginStore: PluginStoreType;
  readonly diagnosticsStore: DiagnosticsStoreType;
  readonly permissionGuard?: PermissionGuardService;
  readonly telemetry?: AdminShellTelemetryService;
  readonly logger?: IAdminShellLogger;
}

/**
 * @alpha
 * Configuration for plugin runtime.
 */
export interface IPluginRuntimeConfig {
  readonly shellVersion: string;
  readonly supportedContractVersion: string;
}

/**
 * @alpha
 * Result of bootstrapping all plugins.
 */
export interface IBootstrapPluginsResult {
  readonly loadedCount: number;
  readonly rejectedCount: number;
  readonly errors: readonly string[];
}

/**
 * @alpha
 * Options for legacy bootstrapPlugins function.
 */
export interface IBootstrapPluginsOptions {
  readonly pluginDescriptors: readonly IAdminDiscoveredPluginDescriptor[];
  readonly pluginStore: PluginStoreType;
  readonly diagnosticsStore: DiagnosticsStoreType;
  readonly permissionGuard?: PermissionGuardService;
  readonly telemetry?: AdminShellTelemetryService;
  readonly logger?: IAdminShellLogger;
  readonly shellVersion: string;
  readonly supportedContractVersion: string;
}
