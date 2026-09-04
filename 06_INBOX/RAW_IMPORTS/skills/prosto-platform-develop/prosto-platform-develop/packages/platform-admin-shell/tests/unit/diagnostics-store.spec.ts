import { beforeEach, describe, expect, it } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useDiagnosticsStore } from '@/entities/diagnostics/index.js';

describe('Diagnostics Store', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  it('should start with empty entries and inactive degraded mode', () => {
    const store = useDiagnosticsStore();

    expect(store.entries).toHaveLength(0);
    expect(store.rejectedCount).toBe(0);
    expect(store.rejectedEntries).toHaveLength(0);
    expect(store.isDegraded).toBe(false);
    expect(store.degradedMode.active).toBe(false);
  });

  it('should add rejected entries', () => {
    const store = useDiagnosticsStore();

    store.addRejected('plugin-a', 'PLUGIN_LOAD_FAILED', 'load error');

    expect(store.entries).toHaveLength(1);
    expect(store.rejectedCount).toBe(1);
    expect(store.entries[0]?.pluginId).toBe('plugin-a');
    expect(store.entries[0]?.reasonCode).toBe('PLUGIN_LOAD_FAILED');
    expect(store.entries[0]?.message).toBe('load error');
    expect(store.entries[0]?.timestamp).toBeInstanceOf(Date);
  });

  it('should add multiple rejected entries', () => {
    const store = useDiagnosticsStore();

    store.addRejected('plugin-a', 'PLUGIN_LOAD_FAILED', 'error a');
    store.addRejected('plugin-b', 'PERMISSION_DENIED', 'error b');
    store.addRejected('plugin-c', 'SHELL_VERSION_MISMATCH', 'error c');

    expect(store.rejectedCount).toBe(3);
    expect(store.rejectedEntries).toHaveLength(3);
  });

  it('should include optional remediationHint', () => {
    const store = useDiagnosticsStore();

    store.addRejected(
      'plugin-a',
      'PERMISSION_DENIED',
      'missing permissions',
      'Grant the permissions',
    );

    expect(store.entries[0]?.remediationHint).toBe('Grant the permissions');
  });

  it('should get rejected entries by plugin id', () => {
    const store = useDiagnosticsStore();

    store.addRejected('plugin-a', 'PLUGIN_LOAD_FAILED', 'error 1');
    store.addRejected('plugin-b', 'PERMISSION_DENIED', 'error 2');
    store.addRejected('plugin-a', 'EXTENSION_DUPLICATE_ID', 'error 3');

    const pluginAEntries = store.getRejectedByPlugin('plugin-a');
    expect(pluginAEntries).toHaveLength(2);
    expect(pluginAEntries[0]?.reasonCode).toBe('PLUGIN_LOAD_FAILED');
    expect(pluginAEntries[1]?.reasonCode).toBe('EXTENSION_DUPLICATE_ID');

    const pluginBEntries = store.getRejectedByPlugin('plugin-b');
    expect(pluginBEntries).toHaveLength(1);

    const pluginCEntries = store.getRejectedByPlugin('nonexistent');
    expect(pluginCEntries).toHaveLength(0);
  });

  it('should enter degraded mode', () => {
    const store = useDiagnosticsStore();

    store.enterDegradedMode('DISCOVERY_NETWORK_ERROR', 'Cannot reach BFF');

    expect(store.isDegraded).toBe(true);
    expect(store.degradedMode.active).toBe(true);
    expect(store.degradedMode.reason).toBe('DISCOVERY_NETWORK_ERROR');
    expect(store.degradedMode.message).toBe('Cannot reach BFF');
    expect(store.degradedMode.timestamp).toBeInstanceOf(Date);
  });

  it('should override degraded mode reason on re-entry', () => {
    const store = useDiagnosticsStore();

    store.enterDegradedMode('DISCOVERY_TIMEOUT', 'Timed out');
    store.enterDegradedMode('PLUGIN_LOAD_FAILURE', 'Plugins failed');

    expect(store.degradedMode.reason).toBe('PLUGIN_LOAD_FAILURE');
    expect(store.degradedMode.message).toBe('Plugins failed');
  });

  it('should exit degraded mode', () => {
    const store = useDiagnosticsStore();

    store.enterDegradedMode('DISCOVERY_NETWORK_ERROR', 'Cannot reach BFF');
    expect(store.isDegraded).toBe(true);

    store.exitDegradedMode();
    expect(store.isDegraded).toBe(false);
    expect(store.degradedMode.active).toBe(false);
    expect(store.degradedMode.reason).toBe('UNKNOWN');
    expect(store.degradedMode.message).toBe('');
  });

  it('should clear all entries and exit degraded mode', () => {
    const store = useDiagnosticsStore();

    store.addRejected('plugin-a', 'PLUGIN_LOAD_FAILED', 'error');
    store.addRejected('plugin-b', 'PERMISSION_DENIED', 'error');
    store.enterDegradedMode('PLUGIN_LOAD_FAILURE', 'Some failed');

    expect(store.rejectedCount).toBe(2);
    expect(store.isDegraded).toBe(true);

    store.clear();

    expect(store.rejectedCount).toBe(0);
    expect(store.entries).toHaveLength(0);
    expect(store.isDegraded).toBe(false);
  });

  it('should track rejectedEntries as reactive computed', () => {
    const store = useDiagnosticsStore();

    expect(store.rejectedEntries).toHaveLength(0);

    store.addRejected('plugin-a', 'PLUGIN_LOAD_FAILED', 'error');

    expect(store.rejectedEntries).toHaveLength(1);
    expect(store.rejectedEntries[0]?.pluginId).toBe('plugin-a');
  });
});
