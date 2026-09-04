import type {
  IAdminDiscoveredPluginDescriptor,
  IAdminUIPluginManifest,
} from '@prosto/platform-admin-contracts';
import { ADMIN_COMPATIBILITY_CONTRACT_VERSION } from '@prosto/platform-admin-contracts';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useDiagnosticsStore } from '@/entities/diagnostics';
import { usePluginStore } from '@/entities/plugin';
import { loadPlugin } from '@/features/plugin-runtime/model/plugin-loader.js';
import { PluginRuntimeService } from '@/features/plugin-runtime';
import { APP_VERSION } from '@/shared/version';

vi.mock('@/features/plugin-runtime/model/plugin-loader.js', () => ({
  loadPlugin: vi.fn().mockResolvedValue(undefined),
}));

function createDescriptor(
  overrides: Pick<IAdminDiscoveredPluginDescriptor, 'id'> &
    Partial<
      Omit<IAdminDiscoveredPluginDescriptor, 'id' | 'extensions'> & {
        extensionPoints: IAdminUIPluginManifest['extensionPoints'];
      }
    >,
): IAdminDiscoveredPluginDescriptor {
  const extPts = overrides.extensionPoints ?? [];
  return {
    id: overrides.id,
    version: overrides.version ?? '1.0.0',
    shellCompatibility: overrides.shellCompatibility ?? '>=0.0.0',
    trustClass: overrides.trustClass ?? 'trusted',
    reviewStatus: overrides.reviewStatus ?? 'approved',
    displayName: overrides.displayName,
    metadata: overrides.metadata,
    extensions: {
      navigation: extPts.includes('nav')
        ? [
            {
              id: `${overrides.id}-nav`,
              pluginId: overrides.id,
              label: overrides.displayName ?? overrides.id,
              order: overrides.metadata?.order
                ? Number(overrides.metadata.order)
                : 0,
            },
          ]
        : [],
      pages: extPts.includes('page')
        ? [
            {
              id: `${overrides.id}-page`,
              pluginId: overrides.id,
              route: `/${overrides.id}`,
              title: overrides.displayName ?? overrides.id,
              componentKey: overrides.id,
              order: overrides.metadata?.order
                ? Number(overrides.metadata.order)
                : 0,
            },
          ]
        : [],
      widgets: extPts.includes('widget')
        ? [
            {
              id: `${overrides.id}-widget`,
              pluginId: overrides.id,
              slot: 'default',
              componentKey: overrides.id,
              order: overrides.metadata?.order
                ? Number(overrides.metadata.order)
                : 0,
            },
          ]
        : [],
      actions: extPts.includes('action')
        ? [
            {
              id: `${overrides.id}-action`,
              pluginId: overrides.id,
              target: overrides.id,
              label: overrides.displayName ?? overrides.id,
              actionKey: overrides.id,
              order: overrides.metadata?.order
                ? Number(overrides.metadata.order)
                : 0,
            },
          ]
        : [],
    },
  };
}

describe('Discovery integration', () => {
  let service: PluginRuntimeService;

  beforeEach(() => {
    setActivePinia(createPinia());
    service = new PluginRuntimeService(
      {
        pluginStore: usePluginStore(),
        diagnosticsStore: useDiagnosticsStore(),
      },
      {
        shellVersion: APP_VERSION,
        supportedContractVersion: ADMIN_COMPATIBILITY_CONTRACT_VERSION,
      },
    );
  });

  it('should bootstrap compatible plugins', async () => {
    const pluginDescriptors: IAdminDiscoveredPluginDescriptor[] = [
      createDescriptor({ id: 'plugin-a' }),
    ];

    await service.bootstrapPlugins(pluginDescriptors);

    const diagStore = useDiagnosticsStore();
    expect(diagStore.rejectedEntries).toHaveLength(0);
  });

  it('should reject incompatible plugins', async () => {
    const pluginDescriptors: IAdminDiscoveredPluginDescriptor[] = [
      createDescriptor({ id: 'plugin-b', shellCompatibility: '>=99.0.0' }),
    ];

    await service.bootstrapPlugins(pluginDescriptors);

    const diagStore = useDiagnosticsStore();
    const rejected = diagStore.rejectedEntries;

    expect(rejected).toHaveLength(1);
    expect(rejected[0]?.reasonCode).toBe('SHELL_VERSION_MISMATCH');
    expect(rejected[0]?.message).toBeDefined();
    expect(rejected[0]?.message).toContain('does not satisfy');
    expect(rejected[0]?.remediationHint).toBeDefined();
    expect(rejected[0]?.remediationHint).toContain(
      'compatible admin shell version',
    );
  });

  it('should register extension points by manifest type', async () => {
    const pluginDescriptors: IAdminDiscoveredPluginDescriptor[] = [
      createDescriptor({ id: 'plugin-nav', extensionPoints: ['nav'] }),
      createDescriptor({ id: 'plugin-page', extensionPoints: ['page'] }),
      createDescriptor({ id: 'plugin-widget', extensionPoints: ['widget'] }),
      createDescriptor({ id: 'plugin-action', extensionPoints: ['action'] }),
    ];

    await service.bootstrapPlugins(pluginDescriptors);

    const registry = service.getExtensionRegistry();
    expect(registry.getNavigationExtensions()).toHaveLength(1);
    expect(registry.getPageExtensions()).toHaveLength(1);
    expect(registry.getWidgetExtensions()).toHaveLength(1);
    expect(registry.getActionExtensions()).toHaveLength(1);
    expect(registry.getTotalExtensionCount()).toBe(4);

    const diagStore = useDiagnosticsStore();
    expect(diagStore.rejectedEntries).toHaveLength(0);
  });

  it('should register multiple extension points from a single plugin', async () => {
    const pluginDescriptors: IAdminDiscoveredPluginDescriptor[] = [
      createDescriptor({
        id: 'plugin-multi',
        extensionPoints: ['nav', 'page', 'widget', 'action'],
      }),
    ];

    await service.bootstrapPlugins(pluginDescriptors);

    expect(service.getExtensionRegistry().getTotalExtensionCount()).toBe(4);

    const diagStore = useDiagnosticsStore();
    expect(diagStore.rejectedEntries).toHaveLength(0);
  });

  it('should isolate plugin failures during bootstrap', async () => {
    vi.mocked(loadPlugin)
      .mockResolvedValueOnce(undefined)
      .mockRejectedValueOnce(new Error('load failed'))
      .mockResolvedValueOnce(undefined);

    const pluginDescriptors: IAdminDiscoveredPluginDescriptor[] = [
      createDescriptor({ id: 'plugin-ok-1', extensionPoints: ['nav'] }),
      createDescriptor({ id: 'plugin-fail', extensionPoints: ['page'] }),
      createDescriptor({ id: 'plugin-ok-2', extensionPoints: ['widget'] }),
    ];

    await service.bootstrapPlugins(pluginDescriptors);

    const diagStore = useDiagnosticsStore();
    const pluginFailDiagnostic = diagStore.rejectedEntries.find(
      (d) => d.pluginId === 'plugin-fail',
    );

    expect(pluginFailDiagnostic).toBeDefined();
    expect(pluginFailDiagnostic?.reasonCode).toBe('PLUGIN_LOAD_FAILED');
    expect(service.getExtensionRegistry().getTotalExtensionCount()).toBe(3);
  });

  it('should sort plugins by order before registration', async () => {
    const pluginDescriptors: IAdminDiscoveredPluginDescriptor[] = [
      createDescriptor({
        id: 'plugin-c',
        extensionPoints: ['nav'],
        metadata: { order: '30' },
      }),
      createDescriptor({
        id: 'plugin-a',
        extensionPoints: ['nav'],
        metadata: { order: '10' },
      }),
      createDescriptor({
        id: 'plugin-b',
        extensionPoints: ['nav'],
        metadata: { order: '20' },
      }),
    ];

    await service.bootstrapPlugins(pluginDescriptors);

    const navs = service.getExtensionRegistry().getNavigationExtensions();

    expect(navs[0]?.pluginId).toBe('plugin-a');
    expect(navs[1]?.pluginId).toBe('plugin-b');
    expect(navs[2]?.pluginId).toBe('plugin-c');
  });

  it('should handle plugins with no extension points', async () => {
    const pluginDescriptors: IAdminDiscoveredPluginDescriptor[] = [
      createDescriptor({ id: 'plugin-plain', extensionPoints: [] }),
    ];

    await service.bootstrapPlugins(pluginDescriptors);

    expect(service.getExtensionRegistry().getTotalExtensionCount()).toBe(0);

    const diagStore = useDiagnosticsStore();
    expect(diagStore.rejectedEntries).toHaveLength(0);
  });

  it('should reject plugin with invalid manifest schema version', async () => {
    const convertSpy = vi.spyOn(
      await import('@prosto/platform-admin-contracts'),
      'convertDescriptorToManifest',
    );

    const invalidManifest: IAdminUIPluginManifest = {
      id: 'plugin-invalid-schema',
      version: '1.0.0',
      schemaVersion:
        'admin-ui-plugin-manifest.v2' as 'admin-ui-plugin-manifest.v1',
      shellCompatibility: '>=0.0.0',
      extensionPoints: [],
      requiredPermissions: [],
      requiredCapabilities: [],
      trustClass: 'trusted',
      reviewStatus: 'approved',
    };

    convertSpy.mockReturnValueOnce(invalidManifest);

    const pluginDescriptors: IAdminDiscoveredPluginDescriptor[] = [
      createDescriptor({ id: 'plugin-invalid-schema' }),
    ];

    await service.bootstrapPlugins(pluginDescriptors);

    const diagStore = useDiagnosticsStore();
    const rejected = diagStore.rejectedEntries;

    expect(rejected).toHaveLength(1);
    expect(rejected[0]?.reasonCode).toBe('PLUGIN_MANIFEST_INVALID');
    expect(rejected[0]?.message).toContain(
      'Unsupported plugin manifest schema',
    );

    convertSpy.mockRestore();
  });

  it('should continue bootstrap after rejecting incompatible plugin', async () => {
    const pluginDescriptors: IAdminDiscoveredPluginDescriptor[] = [
      createDescriptor({
        id: 'plugin-incompatible',
        shellCompatibility: '>=99.0.0',
        extensionPoints: ['nav'],
      }),
      createDescriptor({ id: 'plugin-ok', extensionPoints: ['page'] }),
    ];

    await service.bootstrapPlugins(pluginDescriptors);

    const diagStore = useDiagnosticsStore();
    const rejected = diagStore.rejectedEntries;

    expect(rejected).toHaveLength(1);
    expect(rejected[0]?.pluginId).toBe('plugin-incompatible');
    expect(rejected[0]?.reasonCode).toBe('SHELL_VERSION_MISMATCH');
    expect(rejected[0]?.message).toBeDefined();
    expect(rejected[0]?.remediationHint).toBeDefined();
    expect(service.getExtensionRegistry().getPageExtensions()).toHaveLength(1);
  });
});
