import type {
  IAdminPluginDiscoveryExtensions,
  IAdminUIPluginManifest,
} from '@prosto/platform-admin-contracts';
import { computed, ref } from 'vue';
import {
  type IPermissionFilteredExtensions,
  type IPluginPermissionDecision,
  PermissionGuardService,
} from '../model/permission-guard.service';

const userPermissions = ref<string[]>([]);

/**
 * Vue composable providing permission-aware rendering guards.
 *
 * Wraps the framework-agnostic `PermissionGuardService` and exposes reactive
 * user permissions that can be updated from an auth provider or BFF.
 *
 * @example
 * ```typescript
 * const { setUserPermissions, canRenderPlugin, filterExtensions } = usePermissions();
 *
 * setUserPermissions(['admin', 'plugins.read']);
 *
 * const decision = canRenderPlugin(manifest);
 * const filtered = filterExtensions(pluginExtensions);
 * ```
 */
export function usePermissions() {
  const currentPermissions = computed(() => [...userPermissions.value]);
  const permissionGuardService = computed(
    () => new PermissionGuardService(userPermissions.value),
  );

  function setUserPermissions(permissions: string[]): void {
    userPermissions.value = [...permissions];
  }

  function getUserPermissions(): readonly string[] {
    return [...userPermissions.value];
  }

  function hasPermission(userPerms: string[], required: string[]): boolean {
    return new PermissionGuardService(userPerms).hasPermission(required);
  }

  function canRenderPlugin(
    manifest: IAdminUIPluginManifest,
  ): IPluginPermissionDecision {
    return permissionGuardService.value.evaluatePluginAccess(manifest);
  }

  function canRenderDescriptor(
    descriptorMetadata: Readonly<Record<string, string>> | undefined,
  ): boolean {
    return permissionGuardService.value.evaluateDescriptorAccess(
      descriptorMetadata,
    );
  }

  function filterExtensions(
    extensions: IAdminPluginDiscoveryExtensions,
  ): IPermissionFilteredExtensions {
    return permissionGuardService.value.filterExtensions(extensions);
  }

  return {
    currentPermissions,
    setUserPermissions,
    getUserPermissions,
    hasPermission,
    canRenderPlugin,
    canRenderDescriptor,
    filterExtensions,
  };
}
