import type { IAdminDiscoveredPluginDescriptor } from '@prosto/platform-admin-contracts';
import {
  ADMIN_COMPATIBILITY_CONTRACT_VERSION,
  AdminPluginCompatibilityEvaluator,
  convertDescriptorToManifest,
} from '@prosto/platform-admin-contracts';
import { ExtensionRegistry } from './extension-registry.js';
import { loadPlugin } from './plugin-loader.js';
import { AdminShellLogEvents, AdminShellPhase } from '@/shared/observability';
import type {
  IBootstrapPluginsResult,
  IPluginRuntimeConfig,
  IPluginRuntimeDependencies,
} from './plugin-runtime.types.js';
import { mapExtensionConflictReason } from './plugin-runtime.constants.js';
import {
  createManifestExtensionDescriptors,
  getRegisteredDescriptors,
  hasExtensions,
  sortPluginDescriptors,
} from './plugin-runtime.mappers.js';

/**
 * @alpha
 * Application service for plugin runtime lifecycle.
 *
 * Responsibilities:
 * - Compatibility check for discovered plugins
 * - Permission gating
 * - Extension registration in ExtensionRegistry
 * - Plugin dynamic import (load)
 * - Telemetry and diagnostics reporting
 *
 * Framework-agnostic: does not import Vue, Pinia, or Vuetify.
 *
 * @example
 * ```ts
 * const service = new PluginRuntimeService(
 *   {
 *     pluginStore,
 *     diagnosticsStore,
 *     permissionGuard,
 *     telemetry,
 *     logger,
 *   },
 *   {
 *     shellVersion: '1.0.0',
 *     supportedContractVersion: '1.0.0',
 *   },
 * );
 *
 * const result = await service.bootstrapPlugins(pluginDescriptors);
 * console.log(`Loaded: ${result.loadedCount}, Rejected: ${result.rejectedCount}`);
 * ```
 */
export class PluginRuntimeService {
  private readonly extensionRegistry: ExtensionRegistry;
  private readonly compatibilityEvaluator: AdminPluginCompatibilityEvaluator;

  constructor(
    private readonly dependencies: IPluginRuntimeDependencies,
    private readonly config: IPluginRuntimeConfig,
  ) {
    this.extensionRegistry = new ExtensionRegistry();
    this.compatibilityEvaluator = new AdminPluginCompatibilityEvaluator();
  }

  /**
   * Bootstrap all discovered plugins.
   * Iterates through sorted descriptors, checks compatibility,
   * checks permissions, registers extensions, and loads entry points.
   */
  async bootstrapPlugins(
    pluginDescriptors: readonly IAdminDiscoveredPluginDescriptor[],
  ): Promise<IBootstrapPluginsResult> {
    const {
      pluginStore,
      diagnosticsStore,
      permissionGuard,
      telemetry,
      logger,
    } = this.dependencies;

    logger?.info('Plugin bootstrap started', {
      phase: AdminShellPhase.PLUGIN_REGISTRATION,
      event: AdminShellLogEvents.PLUGIN_REGISTERED,
      descriptorCount: pluginDescriptors.length,
    });

    let loadedCount = 0;
    let rejectedCount = 0;
    const errors: string[] = [];

    for (const pluginDescriptor of sortPluginDescriptors(pluginDescriptors)) {
      const manifest = convertDescriptorToManifest(pluginDescriptor);

      pluginStore.register(manifest);

      const compatibilityResult = this.compatibilityEvaluator.evaluate({
        manifest,
        shellVersion: this.config.shellVersion,
        supportedContractVersion: this.config.supportedContractVersion,
        pluginContractVersion: ADMIN_COMPATIBILITY_CONTRACT_VERSION,
      });

      telemetry?.recordPluginCompatibilityChecked(
        manifest.id,
        compatibilityResult.allowed,
        compatibilityResult.allowed
          ? undefined
          : compatibilityResult.reasonCode,
      );

      if (!compatibilityResult.allowed) {
        pluginStore.markRejected(manifest.id, compatibilityResult.reasonCode);
        this.rejectPlugin({
          pluginId: manifest.id,
          reasonCode: compatibilityResult.reasonCode,
          message: compatibilityResult.message,
          remediationHint: compatibilityResult.remediationHint,
        });
        rejectedCount++;
        continue;
      }

      if (permissionGuard) {
        const permissionDecision =
          permissionGuard.evaluatePluginAccess(manifest);

        telemetry?.recordPluginPermissionChecked(
          manifest.id,
          permissionDecision.allowed,
          permissionDecision.allowed
            ? undefined
            : permissionDecision.missingPermissions,
        );

        if (!permissionDecision.allowed) {
          const missing = permissionDecision.missingPermissions.join(', ');
          const message = `Plugin "${manifest.id}" requires permissions: ${missing}`;
          const remediationHint = `Grant the missing permissions to the current user or role to enable this plugin.`;

          pluginStore.markRejected(manifest.id, 'PERMISSION_DENIED');
          this.rejectPlugin({
            pluginId: manifest.id,
            reasonCode: 'PERMISSION_DENIED',
            message,
            remediationHint,
          });
          rejectedCount++;
          continue;
        }
      }

      const extensions = createManifestExtensionDescriptors(manifest);

      if (hasExtensions(extensions)) {
        const registrationResult =
          this.extensionRegistry.registerPluginExtensions(
            manifest.id,
            extensions,
          );

        if (!registrationResult.registered) {
          for (const conflict of registrationResult.conflicts) {
            const reasonCode = mapExtensionConflictReason(conflict.reason);

            pluginStore.markRejected(manifest.id, reasonCode);
            this.rejectPlugin({
              pluginId: manifest.id,
              reasonCode,
              message: conflict.detail,
            });
            telemetry?.recordExtensionConflict(
              manifest.id,
              conflict.kind,
              conflict.conflictingDescriptorId,
              reasonCode,
            );
          }
          rejectedCount++;
          continue;
        }

        for (const descriptor of getRegisteredDescriptors(extensions)) {
          telemetry?.recordExtensionRegistered(
            manifest.id,
            descriptor.kind,
            descriptor.id,
          );
        }
      }

      telemetry?.recordPluginLoadStarted(manifest.id);

      const loadStartTime = performance.now();

      try {
        await loadPlugin(manifest);
        const loadDurationMs = performance.now() - loadStartTime;

        pluginStore.markReady(manifest.id);
        telemetry?.recordPluginLoadCompleted(manifest.id, loadDurationMs);
        loadedCount++;
      } catch (error) {
        const message = error instanceof Error ? error.message : String(error);

        pluginStore.markFailed(manifest.id, message);
        diagnosticsStore.addRejected(
          manifest.id,
          'PLUGIN_LOAD_FAILED',
          message,
        );
        telemetry?.recordPluginLoadFailed(manifest.id, message);
        errors.push(`${manifest.id}: ${message}`);
        rejectedCount++;
      }
    }

    return { loadedCount, rejectedCount, errors };
  }

  /**
   * Get the extension registry instance.
   */
  getExtensionRegistry(): ExtensionRegistry {
    return this.extensionRegistry;
  }

  /**
   * Clear all registered extensions and reset state.
   * Useful for tests and future hot reload.
   */
  clear(): void {
    this.extensionRegistry.clear();
  }

  private rejectPlugin(options: {
    readonly pluginId: string;
    readonly reasonCode: string;
    readonly message?: string;
    readonly remediationHint?: string;
  }): void {
    const { diagnosticsStore, telemetry } = this.dependencies;

    diagnosticsStore.addRejected(
      options.pluginId,
      options.reasonCode,
      options.message,
      options.remediationHint,
    );
    telemetry?.recordPluginRejected(
      options.pluginId,
      options.reasonCode,
      options.message,
    );
  }
}
