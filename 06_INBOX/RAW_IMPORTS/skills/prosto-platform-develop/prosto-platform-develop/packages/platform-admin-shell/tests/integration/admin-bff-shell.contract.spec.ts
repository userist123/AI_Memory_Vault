import type { IAdminDiscoveredPluginDescriptor } from '@prosto/platform-admin-contracts';
import { ADMIN_COMPATIBILITY_CONTRACT_VERSION } from '@prosto/platform-admin-contracts';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { AdminDiscoveryClient } from '@/shared/api/admin-discovery/index.js';
import { useDiagnosticsStore } from '@/entities/diagnostics';
import { usePluginStore } from '@/entities/plugin';
import { PluginRuntimeService } from '@/features/plugin-runtime';
import { loadPlugin } from '@/features/plugin-runtime/model/plugin-loader.js';
import { PermissionGuardService } from '@/features/permissions';
import { APP_VERSION } from '@/shared/version';
import {
  createDiscoveryPayload,
  createMixedPluginFixtures,
  createNoEntryPointPlugin,
  createPermissionGatedPlugin,
  manifestToDescriptor,
} from './fixtures/plugin-manifests.js';
import { MockBffServer } from './fixtures/mock-bff-server.js';

vi.mock('@/features/plugin-runtime/model/plugin-loader.js', () => ({
  loadPlugin: vi.fn().mockResolvedValue(undefined),
}));

function createDescriptor(
  overrides: Pick<IAdminDiscoveredPluginDescriptor, 'id'> &
    Partial<
      Omit<IAdminDiscoveredPluginDescriptor, 'id' | 'extensions'> & {
        extensionPoints: ('nav' | 'page' | 'widget' | 'action')[];
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

function createService(
  overrides?: Partial<ConstructorParameters<typeof PluginRuntimeService>[0]>,
): PluginRuntimeService {
  return new PluginRuntimeService(
    {
      pluginStore: usePluginStore(),
      diagnosticsStore: useDiagnosticsStore(),
      ...overrides,
    },
    {
      shellVersion: APP_VERSION,
      supportedContractVersion: ADMIN_COMPATIBILITY_CONTRACT_VERSION,
    },
  );
}

describe('Admin BFF → Shell integration contract tests', () => {
  let service: PluginRuntimeService;

  beforeEach(() => {
    setActivePinia(createPinia());
    service = createService();
    vi.mocked(loadPlugin).mockReset();
    vi.mocked(loadPlugin).mockResolvedValue(undefined);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  describe('Discovery payload retrieval via mock BFF', () => {
    it('should retrieve discovery payload from BFF and parse it correctly', async () => {
      const descriptor = createDescriptor({
        id: 'plugin-alpha',
        extensionPoints: ['nav'],
      });
      const mockBff = new MockBffServer().withDiscoveryPayload([descriptor]);

      const client = new AdminDiscoveryClient({
        baseUrl: 'http://mock-bff:3001',
        fetch: mockBff.buildFetch(),
      });

      const result = await client.getDiscovery();

      expect(result.success).toBe(true);

      if (result.success) {
        expect(result.payload.plugins).toHaveLength(1);
        expect(result.payload.plugins[0]?.id).toBe('plugin-alpha');
        expect(result.diagnostics.acceptedCount).toBe(1);
        expect(result.diagnostics.rejectedCount).toBe(0);
      }
    });

    it('should propagate BFF diagnostics into client result', async () => {
      const mockBff = new MockBffServer()
        .withDiscoveryPayload([])
        .withDiagnostics({ duration: 150, acceptedCount: 0, rejectedCount: 3 });

      const client = new AdminDiscoveryClient({
        baseUrl: 'http://mock-bff:3001',
        fetch: mockBff.buildFetch(),
      });

      const result = await client.getDiscovery();

      expect(result.success).toBe(true);

      if (result.success) {
        expect(result.diagnostics.duration).toBe(150);
        expect(result.diagnostics.rejectedCount).toBe(3);
      }
    });

    it('should handle BFF returning network error', async () => {
      const mockBff = new MockBffServer().withNetworkError(
        new Error('ECONNREFUSED'),
      );

      const client = new AdminDiscoveryClient({
        baseUrl: 'http://mock-bff:3001',
        fetch: mockBff.buildFetch(),
      });

      const result = await client.getDiscovery();

      expect(result.success).toBe(false);

      if (!result.success) {
        expect(result.reason).toBe('NETWORK_ERROR');
      }
    });

    it('should handle BFF returning HTTP 500', async () => {
      const mockBff = new MockBffServer().withHttpStatus(500);

      const client = new AdminDiscoveryClient({
        baseUrl: 'http://mock-bff:3001',
        fetch: mockBff.buildFetch(),
      });

      const result = await client.getDiscovery();

      expect(result.success).toBe(false);

      if (!result.success && 'statusCode' in result) {
        expect(result.reason).toBe('HTTP_ERROR');
        expect(result.statusCode).toBe(500);
      }
    });

    it('should call the correct BFF discovery endpoint', async () => {
      const mockBff = new MockBffServer().withDiscoveryPayload([]);

      const client = new AdminDiscoveryClient({
        baseUrl: 'http://mock-bff:3001',
        fetch: mockBff.buildFetch(),
      });

      await client.getDiscovery();

      expect(mockBff.requestLog).toHaveLength(1);
      expect(mockBff.requestLog[0]?.url).toBe(
        'http://mock-bff:3001/admin/api/v1/discovery',
      );
      expect(mockBff.requestLog[0]?.method).toBe('GET');
    });
  });

  describe('Mixed plugin manifest fixtures — bootstrap integration', () => {
    it('should bootstrap compatible plugins and reject incompatible ones', async () => {
      const { compatiblePlugins, incompatiblePlugins } =
        createMixedPluginFixtures();
      const allDescriptors = [...compatiblePlugins, ...incompatiblePlugins];

      await service.bootstrapPlugins(allDescriptors);

      const diagStore = useDiagnosticsStore();

      expect(diagStore.rejectedEntries.length).toBeGreaterThanOrEqual(1);

      const rejectedIds = diagStore.rejectedEntries.map((e) => e.pluginId);
      expect(rejectedIds).toContain('plugin-old-d');
    });

    it('should register all extension points from compatible plugins', async () => {
      const { compatiblePlugins } = createMixedPluginFixtures();

      await service.bootstrapPlugins(compatiblePlugins);

      expect(
        service.getExtensionRegistry().getNavigationExtensions().length,
      ).toBeGreaterThanOrEqual(2);
      expect(
        service.getExtensionRegistry().getPageExtensions().length,
      ).toBeGreaterThanOrEqual(2);
      expect(service.getExtensionRegistry().getWidgetExtensions()).toHaveLength(
        1,
      );
      expect(service.getExtensionRegistry().getActionExtensions()).toHaveLength(
        1,
      );
    });

    it('should handle empty discovery payload gracefully', async () => {
      const payload = createDiscoveryPayload([]);

      await service.bootstrapPlugins(payload.plugins);

      expect(service.getExtensionRegistry().getTotalExtensionCount()).toBe(0);

      const diagStore = useDiagnosticsStore();
      expect(diagStore.rejectedEntries).toHaveLength(0);
    });

    it('should sort plugins by metadata order before registration', async () => {
      const descriptors = [
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

      await service.bootstrapPlugins(descriptors);

      const navs = service.getExtensionRegistry().getNavigationExtensions();
      expect(navs[0]?.pluginId).toBe('plugin-a');
      expect(navs[1]?.pluginId).toBe('plugin-b');
      expect(navs[2]?.pluginId).toBe('plugin-c');
    });
  });

  describe('Permission-aware rendering guards with discovery payload', () => {
    it('should reject plugin when user lacks required permissions', async () => {
      const manifest = createPermissionGatedPlugin('plugin-secure', [
        'admin',
        'plugins.write',
      ]);
      const descriptor = manifestToDescriptor(manifest);
      const guard = new PermissionGuardService(['admin']);

      const guardService = createService({ permissionGuard: guard });
      await guardService.bootstrapPlugins([descriptor]);

      const diagStore = useDiagnosticsStore();
      const rejected = diagStore.rejectedEntries.find(
        (e) => e.pluginId === 'plugin-secure',
      );

      expect(rejected).toBeDefined();
      expect(rejected?.reasonCode).toBe('PERMISSION_DENIED');
    });

    it('should accept plugin when user has all required permissions', async () => {
      const manifest = createPermissionGatedPlugin('plugin-secure', [
        'admin',
        'plugins.write',
      ]);
      const descriptor = manifestToDescriptor(manifest);
      const guard = new PermissionGuardService(['admin', 'plugins.write']);

      const guardService = createService({ permissionGuard: guard });
      await guardService.bootstrapPlugins([descriptor]);

      const diagStore = useDiagnosticsStore();
      const rejected = diagStore.rejectedEntries.find(
        (e) => e.pluginId === 'plugin-secure',
      );

      expect(rejected).toBeUndefined();
      expect(guardService.getExtensionRegistry().getTotalExtensionCount()).toBe(
        2,
      );
    });

    it('should filter extensions based on per-extension permission metadata', () => {
      const guard = new PermissionGuardService(['admin', 'plugins.read']);

      const extensions = {
        navigation: [
          {
            id: 'nav-1',
            pluginId: 'p1',
            label: 'Nav',
            metadata: { requiredPermissions: JSON.stringify(['plugins.read']) },
          },
        ],
        pages: [
          {
            id: 'page-1',
            pluginId: 'p1',
            route: '/page',
            title: 'Page',
            componentKey: 'PageView',
            metadata: {
              requiredPermissions: JSON.stringify(['admin', 'plugins.write']),
            },
          },
        ],
        widgets: [],
        actions: [],
      };

      const filtered = guard.filterExtensions(extensions);

      expect(filtered.navigation).toHaveLength(1);
      expect(filtered.pages).toHaveLength(0);
    });

    it('should handle mixed permission scenarios in discovery payload', async () => {
      const descriptors = [
        createDescriptor({ id: 'plugin-public', extensionPoints: ['nav'] }),
        manifestToDescriptor(
          createPermissionGatedPlugin('plugin-admin-only', ['admin.manage']),
        ),
        manifestToDescriptor(
          createPermissionGatedPlugin('plugin-super-admin', ['super.admin']),
        ),
      ];

      const guard = new PermissionGuardService(['admin.manage']);

      const guardService = createService({ permissionGuard: guard });
      await guardService.bootstrapPlugins(descriptors);

      const diagStore = useDiagnosticsStore();
      const rejectedIds = diagStore.rejectedEntries.map((e) => e.pluginId);

      expect(rejectedIds).toContain('plugin-super-admin');
      expect(rejectedIds).not.toContain('plugin-public');
      expect(rejectedIds).not.toContain('plugin-admin-only');
    });
  });

  describe('Degraded mode and plugin failure isolation', () => {
    it('should continue bootstrap after plugin load failure', async () => {
      vi.mocked(loadPlugin)
        .mockResolvedValueOnce(undefined)
        .mockRejectedValueOnce(new Error('Dynamic import failed'))
        .mockResolvedValueOnce(undefined);

      const descriptors = [
        createDescriptor({ id: 'plugin-ok-1', extensionPoints: ['nav'] }),
        createDescriptor({ id: 'plugin-fail', extensionPoints: ['page'] }),
        createDescriptor({ id: 'plugin-ok-2', extensionPoints: ['widget'] }),
      ];

      await service.bootstrapPlugins(descriptors);

      const diagStore = useDiagnosticsStore();
      const failedEntry = diagStore.rejectedEntries.find(
        (e) => e.pluginId === 'plugin-fail',
      );

      expect(failedEntry).toBeDefined();
      expect(failedEntry?.reasonCode).toBe('PLUGIN_LOAD_FAILED');
      expect(service.getExtensionRegistry().getTotalExtensionCount()).toBe(3);
    });

    it('should reject plugin without entry point', async () => {
      vi.mocked(loadPlugin).mockRejectedValueOnce(
        new Error('Plugin plugin-no-entry has no entry point'),
      );

      const manifest = createNoEntryPointPlugin('plugin-no-entry');
      const descriptor = manifestToDescriptor(manifest);

      await service.bootstrapPlugins([descriptor]);

      const diagStore = useDiagnosticsStore();
      const rejected = diagStore.rejectedEntries.find(
        (e) => e.pluginId === 'plugin-no-entry',
      );

      expect(rejected).toBeDefined();
      expect(rejected?.reasonCode).toBe('PLUGIN_LOAD_FAILED');
      expect(service.getExtensionRegistry().getTotalExtensionCount()).toBe(1);
    });

    it('should handle all plugins failing gracefully', async () => {
      vi.mocked(loadPlugin).mockRejectedValue(
        new Error('Catastrophic failure'),
      );

      const descriptors = [
        createDescriptor({ id: 'plugin-fail-1', extensionPoints: ['nav'] }),
        createDescriptor({ id: 'plugin-fail-2', extensionPoints: ['page'] }),
      ];

      await service.bootstrapPlugins(descriptors);

      const diagStore = useDiagnosticsStore();
      expect(diagStore.rejectedEntries).toHaveLength(2);
      expect(service.getExtensionRegistry().getTotalExtensionCount()).toBe(2);
    });

    it('should handle mix of compatible, incompatible, and failing plugins', async () => {
      vi.mocked(loadPlugin)
        .mockResolvedValueOnce(undefined)
        .mockRejectedValueOnce(new Error('Load error'))
        .mockResolvedValueOnce(undefined);

      const descriptors = [
        createDescriptor({ id: 'plugin-ok', extensionPoints: ['nav'] }),
        createDescriptor({
          id: 'plugin-incompatible',
          shellCompatibility: '>=99.0.0',
          extensionPoints: ['page'],
        }),
        createDescriptor({
          id: 'plugin-load-fail',
          extensionPoints: ['widget'],
        }),
        createDescriptor({ id: 'plugin-ok-2', extensionPoints: ['action'] }),
      ];

      await service.bootstrapPlugins(descriptors);

      const diagStore = useDiagnosticsStore();
      const rejectedIds = diagStore.rejectedEntries.map((e) => e.pluginId);

      expect(rejectedIds).toContain('plugin-incompatible');
      expect(rejectedIds).toContain('plugin-load-fail');
      expect(service.getExtensionRegistry().getTotalExtensionCount()).toBe(3);
    });
  });

  describe('End-to-end: BFF client → bootstrapPlugins integration', () => {
    it('should fetch from mock BFF and bootstrap plugins end-to-end', async () => {
      const descriptors = [
        createDescriptor({ id: 'e2e-nav', extensionPoints: ['nav'] }),
        createDescriptor({ id: 'e2e-page', extensionPoints: ['page'] }),
        createDescriptor({
          id: 'e2e-incompatible',
          shellCompatibility: '>=99.0.0',
        }),
      ];

      const mockBff = new MockBffServer().withDiscoveryPayload(descriptors);

      const client = new AdminDiscoveryClient({
        baseUrl: 'http://mock-bff:3001',
        fetch: mockBff.buildFetch(),
      });

      const result = await client.getDiscovery();

      expect(result.success).toBe(true);

      if (result.success) {
        await service.bootstrapPlugins(result.payload.plugins);

        const diagStore = useDiagnosticsStore();
        const rejectedIds = diagStore.rejectedEntries.map((e) => e.pluginId);
        expect(rejectedIds).toContain('e2e-incompatible');
        expect(rejectedIds).not.toContain('e2e-nav');
        expect(rejectedIds).not.toContain('e2e-page');

        expect(
          service.getExtensionRegistry().getNavigationExtensions(),
        ).toHaveLength(1);
        expect(service.getExtensionRegistry().getPageExtensions()).toHaveLength(
          1,
        );
      }
    });

    it('should handle BFF returning rejected plugins in payload', async () => {
      const descriptors = [
        createDescriptor({ id: 'plugin-valid', extensionPoints: ['nav'] }),
      ];

      const rejected = [
        {
          id: 'plugin-rejected-by-bff' as const,
          reasonCode: 'TRUST_CLASS_DENIED' as const,
          message: 'Plugin rejected by BFF policy',
          remediationHint: 'Review trust class settings',
        },
      ];

      const mockBff = new MockBffServer().withDiscoveryPayload(
        descriptors,
        rejected,
      );

      const client = new AdminDiscoveryClient({
        baseUrl: 'http://mock-bff:3001',
        fetch: mockBff.buildFetch(),
      });

      const result = await client.getDiscovery();

      expect(result.success).toBe(true);

      if (result.success) {
        expect(result.payload.rejected).toHaveLength(1);
        expect(result.payload.rejected[0]?.id).toBe('plugin-rejected-by-bff');
        expect(result.payload.rejected[0]?.reasonCode).toBe(
          'TRUST_CLASS_DENIED',
        );

        await service.bootstrapPlugins(result.payload.plugins);

        const diagStore = useDiagnosticsStore();
        expect(diagStore.rejectedEntries).toHaveLength(0);

        expect(
          service.getExtensionRegistry().getNavigationExtensions(),
        ).toHaveLength(1);
      }
    });

    it('should handle BFF returning empty plugins with rejected entries', async () => {
      const rejected = [
        {
          id: 'plugin-r1' as const,
          reasonCode: 'SHELL_VERSION_MISMATCH' as const,
          message: 'Incompatible shell version',
          remediationHint: 'Update admin shell',
        },
        {
          id: 'plugin-r2' as const,
          reasonCode: 'TRUST_CLASS_DENIED' as const,
          message: 'Untrusted plugin',
          remediationHint: 'Review trust policy',
        },
      ];

      const mockBff = new MockBffServer().withDiscoveryPayload([], rejected);

      const client = new AdminDiscoveryClient({
        baseUrl: 'http://mock-bff:3001',
        fetch: mockBff.buildFetch(),
      });

      const result = await client.getDiscovery();

      expect(result.success).toBe(true);

      if (result.success) {
        expect(result.payload.plugins).toHaveLength(0);
        expect(result.payload.rejected).toHaveLength(2);

        await service.bootstrapPlugins(result.payload.plugins);

        expect(service.getExtensionRegistry().getTotalExtensionCount()).toBe(0);

        const diagStore = useDiagnosticsStore();
        expect(diagStore.rejectedEntries).toHaveLength(0);
      }
    });

    it('should handle multi-plugin extension point registration in sequence', async () => {
      const descriptors = [
        createDescriptor({
          id: 'seq-a',
          extensionPoints: ['nav'],
          metadata: { order: '10' },
        }),
        createDescriptor({
          id: 'seq-b',
          extensionPoints: ['nav'],
          metadata: { order: '20' },
        }),
      ];

      const mockBff = new MockBffServer().withDiscoveryPayload(descriptors);

      const client = new AdminDiscoveryClient({
        baseUrl: 'http://mock-bff:3001',
        fetch: mockBff.buildFetch(),
      });

      const result = await client.getDiscovery();

      expect(result.success).toBe(true);

      if (result.success) {
        await service.bootstrapPlugins(result.payload.plugins);

        const navs = service.getExtensionRegistry().getNavigationExtensions();
        expect(navs).toHaveLength(2);
        expect(navs[0]?.pluginId).toBe('seq-a');
        expect(navs[1]?.pluginId).toBe('seq-b');
      }
    });
  });
});
