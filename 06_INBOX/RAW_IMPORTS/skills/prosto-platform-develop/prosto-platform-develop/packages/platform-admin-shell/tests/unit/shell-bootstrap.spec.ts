import type { IAdminDiscoveredPluginDescriptor } from '@prosto/platform-admin-contracts';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useDiagnosticsStore } from '@/entities/diagnostics';
import { usePluginStore } from '@/entities/plugin';
import { shellBootstrap } from '@/processes/admin-shell-bootstrap';
import type { IShellBootstrapOptions } from '@/processes/admin-shell-bootstrap';

function createDescriptor(
  id: string,
  overrides?: Partial<IAdminDiscoveredPluginDescriptor>,
): IAdminDiscoveredPluginDescriptor {
  return {
    id,
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

function createMockDiscoveryClient(
  plugins: IAdminDiscoveredPluginDescriptor[],
) {
  return {
    getDiscovery: vi.fn().mockResolvedValue({
      success: true,
      payload: {
        schemaVersion: 'admin-discovery-payload.v1',
        generatedAt: new Date().toISOString(),
        plugins,
        rejected: [],
      },
      correlationId: 'test-correlation',
      diagnostics: {
        acceptedCount: plugins.length,
        rejectedCount: 0,
        duration: 0,
      },
    }),
  };
}

function createFailingDiscoveryClient(
  reason:
    | 'NETWORK_ERROR'
    | 'TIMEOUT'
    | 'HTTP_ERROR'
    | 'UNAUTHENTICATED'
    | 'VALIDATION_FAILED',
  message: string,
) {
  if (reason === 'VALIDATION_FAILED') {
    return {
      getDiscovery: vi.fn().mockResolvedValue({
        success: false,
        reason,
        correlationId: undefined,
        issues: [{ code: 'test', message, path: '$' }],
      }),
    };
  }
  return {
    getDiscovery: vi.fn().mockResolvedValue({
      success: false,
      reason,
      message,
      ...(reason === 'HTTP_ERROR' ? { statusCode: 500 } : {}),
      ...(reason === 'UNAUTHENTICATED' ? { statusCode: 401 } : {}),
    }),
  };
}

function createMockPluginRuntime(overrides?: {
  loadedCount?: number;
  rejectedCount?: number;
}) {
  return {
    bootstrapPlugins: vi.fn().mockResolvedValue({
      loadedCount: overrides?.loadedCount ?? 0,
      rejectedCount: overrides?.rejectedCount ?? 0,
      errors: [],
    }),
  };
}

describe('shellBootstrap', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  it('should return loadedCount equal to ready plugins, not total discovered', async () => {
    const descriptors = [
      createDescriptor('plugin-a'),
      createDescriptor('plugin-fail'),
      createDescriptor('plugin-b'),
    ];

    const options: IShellBootstrapOptions = {
      discoveryClient: createMockDiscoveryClient(descriptors),
      pluginRuntime: createMockPluginRuntime({
        loadedCount: 2,
        rejectedCount: 1,
      }),
      pluginStore: usePluginStore(),
      diagnosticsStore: useDiagnosticsStore(),
      navigateToLogin: vi.fn(),
    };

    const result = await shellBootstrap(options);

    expect(result.success).toBe(true);
    expect(result.loadedCount).toBe(2);
    expect(result.rejectedCount).toBe(1);
    expect(result.degraded).toBe(true);
    expect(result.message).toBe('Shell loaded 2 plugin(s).');
  });

  it('should report loadedCount 0 when all plugins fail', async () => {
    const descriptors = [
      createDescriptor('plugin-a'),
      createDescriptor('plugin-b'),
    ];

    const options: IShellBootstrapOptions = {
      discoveryClient: createMockDiscoveryClient(descriptors),
      pluginRuntime: createMockPluginRuntime({
        loadedCount: 0,
        rejectedCount: 2,
      }),
      pluginStore: usePluginStore(),
      diagnosticsStore: useDiagnosticsStore(),
      navigateToLogin: vi.fn(),
    };

    const result = await shellBootstrap(options);

    expect(result.success).toBe(true);
    expect(result.loadedCount).toBe(0);
    expect(result.rejectedCount).toBe(2);
    expect(result.degraded).toBe(true);
  });

  it('should report loadedCount equal to discovered when all succeed', async () => {
    const descriptors = [
      createDescriptor('plugin-a'),
      createDescriptor('plugin-b'),
    ];

    const options: IShellBootstrapOptions = {
      discoveryClient: createMockDiscoveryClient(descriptors),
      pluginRuntime: createMockPluginRuntime({
        loadedCount: 2,
        rejectedCount: 0,
      }),
      pluginStore: usePluginStore(),
      diagnosticsStore: useDiagnosticsStore(),
      navigateToLogin: vi.fn(),
    };

    const result = await shellBootstrap(options);

    expect(result.success).toBe(true);
    expect(result.loadedCount).toBe(2);
    expect(result.rejectedCount).toBe(0);
    expect(result.degraded).toBe(false);
    expect(result.message).toBe('Shell loaded 2 plugin(s).');
  });

  it('should enter degraded mode when any plugin is rejected', async () => {
    const descriptors = [createDescriptor('plugin-a')];

    const options: IShellBootstrapOptions = {
      discoveryClient: createMockDiscoveryClient(descriptors),
      pluginRuntime: createMockPluginRuntime({
        loadedCount: 0,
        rejectedCount: 1,
      }),
      pluginStore: usePluginStore(),
      diagnosticsStore: useDiagnosticsStore(),
      navigateToLogin: vi.fn(),
    };

    const result = await shellBootstrap(options);

    expect(result.degraded).toBe(true);

    const diagStore = useDiagnosticsStore();

    expect(diagStore.isDegraded).toBe(true);
    expect(diagStore.degradedMode.reason).toBe('PLUGIN_LOAD_FAILURE');
  });

  it('should not enter degraded mode when all plugins load successfully', async () => {
    const descriptors = [createDescriptor('plugin-a')];

    const options: IShellBootstrapOptions = {
      discoveryClient: createMockDiscoveryClient(descriptors),
      pluginRuntime: createMockPluginRuntime({
        loadedCount: 1,
        rejectedCount: 0,
      }),
      pluginStore: usePluginStore(),
      diagnosticsStore: useDiagnosticsStore(),
      navigateToLogin: vi.fn(),
    };

    const result = await shellBootstrap(options);

    expect(result.degraded).toBe(false);

    const diagStore = useDiagnosticsStore();

    expect(diagStore.isDegraded).toBe(false);
  });

  it('should set degraded on discovery network error', async () => {
    const options: IShellBootstrapOptions = {
      discoveryClient: createFailingDiscoveryClient(
        'NETWORK_ERROR',
        'Connection refused',
      ),
      pluginRuntime: createMockPluginRuntime(),
      pluginStore: usePluginStore(),
      diagnosticsStore: useDiagnosticsStore(),
      navigateToLogin: vi.fn(),
    };

    const result = await shellBootstrap(options);

    expect(result.success).toBe(false);
    expect(result.degraded).toBe(true);
    expect(result.loadedCount).toBe(0);
    expect(result.rejectedCount).toBe(0);

    const diagStore = useDiagnosticsStore();

    expect(diagStore.isDegraded).toBe(true);
    expect(diagStore.degradedMode.reason).toBe('DISCOVERY_NETWORK_ERROR');
  });

  it('should set degraded on discovery timeout', async () => {
    const options: IShellBootstrapOptions = {
      discoveryClient: createFailingDiscoveryClient(
        'TIMEOUT',
        'Request timed out',
      ),
      pluginRuntime: createMockPluginRuntime(),
      pluginStore: usePluginStore(),
      diagnosticsStore: useDiagnosticsStore(),
      navigateToLogin: vi.fn(),
    };

    const result = await shellBootstrap(options);

    expect(result.degraded).toBe(true);

    const diagStore = useDiagnosticsStore();

    expect(diagStore.degradedMode.reason).toBe('DISCOVERY_TIMEOUT');
  });

  it('should set degraded on HTTP error', async () => {
    const options: IShellBootstrapOptions = {
      discoveryClient: createFailingDiscoveryClient(
        'HTTP_ERROR',
        'Internal Server Error',
      ),
      pluginRuntime: createMockPluginRuntime(),
      pluginStore: usePluginStore(),
      diagnosticsStore: useDiagnosticsStore(),
      navigateToLogin: vi.fn(),
    };

    const result = await shellBootstrap(options);

    expect(result.degraded).toBe(true);

    const diagStore = useDiagnosticsStore();

    expect(diagStore.degradedMode.reason).toBe('DISCOVERY_HTTP_ERROR');
  });

  it('should redirect once without degraded mode when unauthenticated', async () => {
    const navigateToLogin = vi.fn();
    const pluginRuntime = createMockPluginRuntime();
    const diagnosticsStore = useDiagnosticsStore();
    const options: IShellBootstrapOptions = {
      discoveryClient: createFailingDiscoveryClient(
        'UNAUTHENTICATED',
        'Authentication is required.',
      ),
      pluginRuntime,
      pluginStore: usePluginStore(),
      diagnosticsStore,
      navigateToLogin,
    };

    const result = await shellBootstrap(options);

    expect(result).toMatchObject({
      success: false,
      degraded: false,
      loadedCount: 0,
      rejectedCount: 0,
    });
    expect(navigateToLogin).toHaveBeenCalledTimes(1);
    expect(navigateToLogin).toHaveBeenCalledWith();
    expect(diagnosticsStore.isDegraded).toBe(false);
    expect(pluginRuntime.bootstrapPlugins).not.toHaveBeenCalled();
  });
});
