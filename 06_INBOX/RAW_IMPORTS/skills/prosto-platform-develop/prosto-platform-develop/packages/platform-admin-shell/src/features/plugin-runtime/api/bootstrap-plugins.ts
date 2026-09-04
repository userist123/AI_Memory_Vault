import { PluginRuntimeService } from '../model/plugin-runtime.service.js';
import type { IBootstrapPluginsOptions } from '../model/plugin-runtime.types.js';

/**
 * @deprecated Use {@link PluginRuntimeService} directly.
 * Legacy wrapper for backward compatibility during migration.
 *
 * This function creates a PluginRuntimeService instance on every call.
 * Prefer constructing the service explicitly and reusing it across the
 * application lifecycle.
 */
export async function bootstrapPlugins(
  options: IBootstrapPluginsOptions,
): Promise<void> {
  const service = new PluginRuntimeService(
    {
      pluginStore: options.pluginStore,
      diagnosticsStore: options.diagnosticsStore,
      permissionGuard: options.permissionGuard,
      telemetry: options.telemetry,
      logger: options.logger,
    },
    {
      shellVersion: options.shellVersion,
      supportedContractVersion: options.supportedContractVersion,
    },
  );

  await service.bootstrapPlugins(options.pluginDescriptors);
}
