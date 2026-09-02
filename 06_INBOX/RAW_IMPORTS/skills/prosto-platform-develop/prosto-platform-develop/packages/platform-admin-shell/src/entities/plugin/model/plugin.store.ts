import type { IAdminUIPluginManifest } from '@prosto/platform-admin-contracts';
import type { IPluginEntry } from './plugin.types';
import { defineStore } from 'pinia';
import { ref } from 'vue';

export const usePluginStore = defineStore('plugins', () => {
  const plugins = ref(new Map<string, IPluginEntry>());

  function register(manifest: IAdminUIPluginManifest): void {
    plugins.value.set(manifest.id, { manifest, status: 'loading' });
  }

  function markReady(id: string): void {
    const entry = plugins.value.get(id);

    if (entry) {
      entry.status = 'ready';
    }
  }

  function markFailed(id: string, error: string): void {
    const entry = plugins.value.get(id);

    if (entry) {
      entry.status = 'failed';
      entry.error = error;
    }
  }

  function markRejected(id: string, reason: string): void {
    const entry = plugins.value.get(id);

    if (entry) {
      entry.status = 'rejected';
      entry.error = reason;
    }
  }

  function rejectedPlugins(): IPluginEntry[] {
    return Array.from(plugins.value.values()).filter(
      (plugin) => plugin.status === 'rejected',
    );
  }

  function failedPlugins(): IPluginEntry[] {
    return Array.from(plugins.value.values()).filter(
      (plugin) => plugin.status === 'failed',
    );
  }

  function readyPlugins(): IPluginEntry[] {
    return Array.from(plugins.value.values()).filter(
      (plugin) => plugin.status === 'ready',
    );
  }

  function clear(): void {
    plugins.value.clear();
  }

  return {
    plugins,
    register,
    markReady,
    markFailed,
    markRejected,
    rejectedPlugins,
    failedPlugins,
    readyPlugins,
    clear,
  };
});

/**
 * @alpha
 * Runtime type of the plugin store returned by {@link usePluginStore}.
 */
export type PluginStoreType = ReturnType<typeof usePluginStore>;
