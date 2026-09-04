import type { IAdminUIPluginManifest } from '@prosto/platform-admin-contracts';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { usePluginStore } from '@/entities/plugin/index.js';

vi.mock('@/features/plugin-runtime/model/plugin-loader.js', () => ({
  loadPlugin: vi.fn().mockResolvedValue(undefined),
}));

describe('Plugin Registry', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  it('should register plugins', () => {
    const store = usePluginStore();
    const manifest: IAdminUIPluginManifest = {
      id: 'test-plugin',
      version: '1.0.0',
      schemaVersion: 'admin-ui-plugin-manifest.v1',
      shellCompatibility: '>=0.0.0',
      extensionPoints: [],
      requiredPermissions: [],
      requiredCapabilities: [],
      trustClass: 'trusted',
      reviewStatus: 'approved',
    };

    store.register(manifest);

    expect(store.plugins.size).toBe(1);
    expect(store.plugins.get('test-plugin')?.status).toBe('loading');
  });

  it('should mark plugins as ready', () => {
    const store = usePluginStore();
    const manifest: IAdminUIPluginManifest = {
      id: 'test-plugin',
      version: '1.0.0',
      schemaVersion: 'admin-ui-plugin-manifest.v1',
      shellCompatibility: '>=0.0.0',
      extensionPoints: [],
      requiredPermissions: [],
      requiredCapabilities: [],
      trustClass: 'trusted',
      reviewStatus: 'approved',
    };

    store.register(manifest);
    store.markReady('test-plugin');

    expect(store.plugins.get('test-plugin')?.status).toBe('ready');
  });

  it('should mark plugins as failed', () => {
    const store = usePluginStore();
    const manifest: IAdminUIPluginManifest = {
      id: 'test-plugin',
      version: '1.0.0',
      schemaVersion: 'admin-ui-plugin-manifest.v1',
      shellCompatibility: '>=0.0.0',
      extensionPoints: [],
      requiredPermissions: [],
      requiredCapabilities: [],
      trustClass: 'trusted',
      reviewStatus: 'approved',
    };

    store.register(manifest);
    store.markFailed('test-plugin', 'load error');

    expect(store.plugins.get('test-plugin')?.status).toBe('failed');
    expect(store.plugins.get('test-plugin')?.error).toBe('load error');
  });

  it('should list ready plugins', () => {
    const store = usePluginStore();
    const manifest: IAdminUIPluginManifest = {
      id: 'ready-plugin',
      version: '1.0.0',
      schemaVersion: 'admin-ui-plugin-manifest.v1',
      shellCompatibility: '>=0.0.0',
      extensionPoints: [],
      requiredPermissions: [],
      requiredCapabilities: [],
      trustClass: 'trusted',
      reviewStatus: 'approved',
    };

    store.register(manifest);
    store.markReady('ready-plugin');

    expect(store.readyPlugins()).toHaveLength(1);
    expect(store.readyPlugins()[0]?.manifest.id).toBe('ready-plugin');
  });

  it('should mark plugins as rejected', () => {
    const store = usePluginStore();
    const manifest: IAdminUIPluginManifest = {
      id: 'rejected-plugin',
      version: '1.0.0',
      schemaVersion: 'admin-ui-plugin-manifest.v1',
      shellCompatibility: '>=0.0.0',
      extensionPoints: [],
      requiredPermissions: [],
      requiredCapabilities: [],
      trustClass: 'trusted',
      reviewStatus: 'approved',
    };

    store.register(manifest);
    store.markRejected('rejected-plugin', 'SHELL_VERSION_MISMATCH');

    expect(store.plugins.get('rejected-plugin')?.status).toBe('rejected');
    expect(store.plugins.get('rejected-plugin')?.error).toBe(
      'SHELL_VERSION_MISMATCH',
    );
  });

  it('should list rejected plugins', () => {
    const store = usePluginStore();
    const manifestA: IAdminUIPluginManifest = {
      id: 'rejected-a',
      version: '1.0.0',
      schemaVersion: 'admin-ui-plugin-manifest.v1',
      shellCompatibility: '>=0.0.0',
      extensionPoints: [],
      requiredPermissions: [],
      requiredCapabilities: [],
      trustClass: 'trusted',
      reviewStatus: 'approved',
    };
    const manifestB: IAdminUIPluginManifest = {
      id: 'ready-b',
      version: '1.0.0',
      schemaVersion: 'admin-ui-plugin-manifest.v1',
      shellCompatibility: '>=0.0.0',
      extensionPoints: [],
      requiredPermissions: [],
      requiredCapabilities: [],
      trustClass: 'trusted',
      reviewStatus: 'approved',
    };

    store.register(manifestA);
    store.markRejected('rejected-a', 'PERMISSION_DENIED');
    store.register(manifestB);
    store.markReady('ready-b');

    expect(store.rejectedPlugins()).toHaveLength(1);
    expect(store.rejectedPlugins()[0]?.manifest.id).toBe('rejected-a');
    expect(store.readyPlugins()).toHaveLength(1);
    expect(store.readyPlugins()[0]?.manifest.id).toBe('ready-b');
  });

  it('should list failed plugins', () => {
    const store = usePluginStore();
    const manifestA: IAdminUIPluginManifest = {
      id: 'failed-a',
      version: '1.0.0',
      schemaVersion: 'admin-ui-plugin-manifest.v1',
      shellCompatibility: '>=0.0.0',
      extensionPoints: [],
      requiredPermissions: [],
      requiredCapabilities: [],
      trustClass: 'trusted',
      reviewStatus: 'approved',
    };
    const manifestB: IAdminUIPluginManifest = {
      id: 'ready-b',
      version: '1.0.0',
      schemaVersion: 'admin-ui-plugin-manifest.v1',
      shellCompatibility: '>=0.0.0',
      extensionPoints: [],
      requiredPermissions: [],
      requiredCapabilities: [],
      trustClass: 'trusted',
      reviewStatus: 'approved',
    };

    store.register(manifestA);
    store.markFailed('failed-a', 'import error');
    store.register(manifestB);
    store.markReady('ready-b');

    expect(store.failedPlugins()).toHaveLength(1);
    expect(store.failedPlugins()[0]?.manifest.id).toBe('failed-a');
    expect(store.failedPlugins()[0]?.error).toBe('import error');
  });

  it('should not mark unregistered plugins', () => {
    const store = usePluginStore();

    store.markReady('nonexistent');
    store.markFailed('nonexistent', 'error');
    store.markRejected('nonexistent', 'reason');

    expect(store.plugins.size).toBe(0);
  });
});
