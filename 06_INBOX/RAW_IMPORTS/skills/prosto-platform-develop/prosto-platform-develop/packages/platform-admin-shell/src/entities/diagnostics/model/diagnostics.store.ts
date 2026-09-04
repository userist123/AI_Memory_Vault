import { defineStore } from 'pinia';
import { computed, ref } from 'vue';
import type {
  DegradedModeReasonType,
  IDegradedModeContext,
  IDiagnosticEntry,
} from './diagnostics.types';

export const useDiagnosticsStore = defineStore('diagnostics', () => {
  const entries = ref<IDiagnosticEntry[]>([]);
  const degradedMode = ref<IDegradedModeContext>({
    active: false,
    reason: 'UNKNOWN',
    message: '',
    timestamp: new Date(),
  });

  const rejectedEntries = computed(() => entries.value);

  const rejectedCount = computed(() => entries.value.length);

  const isDegraded = computed(() => degradedMode.value.active);

  function addRejected(
    pluginId: string,
    reasonCode: string,
    message?: string,
    remediationHint?: string,
  ): void {
    entries.value.push({
      pluginId,
      reasonCode,
      message,
      remediationHint,
      timestamp: new Date(),
    });
  }

  function getRejectedByPlugin(pluginId: string): IDiagnosticEntry[] {
    return entries.value.filter((entry) => entry.pluginId === pluginId);
  }

  function enterDegradedMode(
    reason: DegradedModeReasonType,
    message: string,
  ): void {
    degradedMode.value = {
      active: true,
      reason,
      message,
      timestamp: new Date(),
    };
  }

  function exitDegradedMode(): void {
    degradedMode.value = {
      active: false,
      reason: 'UNKNOWN',
      message: '',
      timestamp: new Date(),
    };
  }

  function clear(): void {
    entries.value = [];
    exitDegradedMode();
  }

  return {
    entries,
    degradedMode,
    rejectedEntries,
    rejectedCount,
    isDegraded,
    addRejected,
    getRejectedByPlugin,
    enterDegradedMode,
    exitDegradedMode,
    clear,
  };
});

/**
 * Runtime type of the diagnostics store returned by {@link useDiagnosticsStore}.
 */
export type DiagnosticsStoreType = ReturnType<typeof useDiagnosticsStore>;
