import type { IAdminDiscoveredPluginDescriptor } from '@prosto/platform-admin-contracts';
import { ADMIN_COMPATIBILITY_CONTRACT_VERSION } from '@prosto/platform-admin-contracts';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { PluginRuntimeService } from '@/features/plugin-runtime/index.js';
import { usePluginStore } from '@/entities/plugin/index.js';
import { useDiagnosticsStore } from '@/entities/diagnostics/index.js';
import { PermissionGuardService } from '@/features/permissions/index.js';
import { APP_VERSION } from '@/shared/version/index.js';

vi.mock('@/features/plugin-runtime/model/plugin-loader.js', () => ({
  loadPlugin: vi.fn().mockResolvedValue(undefined),
}));

function createDescriptor(
  id: string,
  overrides?: Partial<IAdminDiscoveredPluginDescriptor>,
): IAdminDiscoveredPluginDescriptor {
  return {
    id,
    displayName: id,
    version: '1.0.0',
    shellCompatibility: '>=0.0.0',
    trustClass: 'trusted',
    reviewStatus: 'approved',
    extensions: {
      navigation: [],
      pages: [],
      widgets: [],
      actions: [],
    },
    ...overrides,
  };
}

describe('PluginRuntimeService', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  it('should load all compatible plugins', async () => {
    const { loadPlugin } =
      await import('@/features/plugin-runtime/model/plugin-loader.js');

    vi.mocked(loadPlugin).mockReset().mockResolvedValue(undefined);

    const pluginStore = usePluginStore();
    const diagnosticsStore = useDiagnosticsStore();
    const service = new PluginRuntimeService(
      { pluginStore, diagnosticsStore },
      {
        shellVersion: APP_VERSION,
        supportedContractVersion: ADMIN_COMPATIBILITY_CONTRACT_VERSION,
      },
    );

    const descriptors = [
      createDescriptor('plugin-a'),
      createDescriptor('plugin-b'),
    ];

    const result = await service.bootstrapPlugins(descriptors);

    expect(result.loadedCount).toBe(2);
    expect(result.rejectedCount).toBe(0);
    expect(result.errors).toHaveLength(0);
    expect(pluginStore.readyPlugins()).toHaveLength(2);
    expect(loadPlugin).toHaveBeenCalledTimes(2);
  });

  it('should reject plugin when load fails', async () => {
    const { loadPlugin } =
      await import('@/features/plugin-runtime/model/plugin-loader.js');

    vi.mocked(loadPlugin)
      .mockReset()
      .mockRejectedValueOnce(new Error('import failed'));

    const pluginStore = usePluginStore();
    const diagnosticsStore = useDiagnosticsStore();
    const service = new PluginRuntimeService(
      { pluginStore, diagnosticsStore },
      {
        shellVersion: APP_VERSION,
        supportedContractVersion: ADMIN_COMPATIBILITY_CONTRACT_VERSION,
      },
    );

    const result = await service.bootstrapPlugins([
      createDescriptor('plugin-fail'),
    ]);

    expect(result.loadedCount).toBe(0);
    expect(result.rejectedCount).toBe(1);
    expect(result.errors).toHaveLength(1);
    expect(pluginStore.failedPlugins()).toHaveLength(1);
    expect(diagnosticsStore.rejectedCount).toBe(1);
  });

  it('should reject plugin when permission denied', async () => {
    const { loadPlugin } =
      await import('@/features/plugin-runtime/model/plugin-loader.js');
    vi.mocked(loadPlugin).mockReset().mockResolvedValue(undefined);

    const pluginStore = usePluginStore();
    const diagnosticsStore = useDiagnosticsStore();
    const permissionGuard = new PermissionGuardService(['admin']);
    const service = new PluginRuntimeService(
      { pluginStore, diagnosticsStore, permissionGuard },
      {
        shellVersion: APP_VERSION,
        supportedContractVersion: ADMIN_COMPATIBILITY_CONTRACT_VERSION,
      },
    );

    const descriptor = createDescriptor('plugin-privileged', {
      metadata: {
        requiredPermissions: JSON.stringify(['plugins.admin']),
      },
    });

    const result = await service.bootstrapPlugins([descriptor]);

    expect(result.loadedCount).toBe(0);
    expect(result.rejectedCount).toBe(1);
    expect(pluginStore.rejectedPlugins()).toHaveLength(1);
    expect(loadPlugin).not.toHaveBeenCalled();
  });

  it('should isolate registries between service instances', async () => {
    const pluginStore = usePluginStore();
    const diagnosticsStore = useDiagnosticsStore();
    const serviceA = new PluginRuntimeService(
      { pluginStore, diagnosticsStore },
      {
        shellVersion: APP_VERSION,
        supportedContractVersion: ADMIN_COMPATIBILITY_CONTRACT_VERSION,
      },
    );
    const serviceB = new PluginRuntimeService(
      { pluginStore, diagnosticsStore },
      {
        shellVersion: APP_VERSION,
        supportedContractVersion: ADMIN_COMPATIBILITY_CONTRACT_VERSION,
      },
    );

    expect(serviceA.getExtensionRegistry()).not.toBe(
      serviceB.getExtensionRegistry(),
    );
  });
});
