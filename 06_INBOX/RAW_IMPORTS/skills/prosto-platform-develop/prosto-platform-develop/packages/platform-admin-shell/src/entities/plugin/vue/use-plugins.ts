import { computed } from 'vue';
import { usePluginStore } from '../model/plugin.store.js';

export function usePlugins() {
  const store = usePluginStore();

  const readyPlugins = computed(() => store.readyPlugins());
  const rejectedPlugins = computed(() => store.rejectedPlugins());
  const pluginCount = computed(() => store.plugins.size);

  return {
    readyPlugins,
    rejectedPlugins,
    pluginCount,
  };
}
