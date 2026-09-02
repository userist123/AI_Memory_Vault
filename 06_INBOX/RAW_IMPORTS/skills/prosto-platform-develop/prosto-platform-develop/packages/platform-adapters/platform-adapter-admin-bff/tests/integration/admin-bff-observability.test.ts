import type {
  IAdminPermissionPolicy,
  IAdminUIPluginManifest,
} from '@prosto/platform-admin-contracts';
import {
  ADMIN_COMPATIBILITY_CONTRACT_VERSION,
  ADMIN_PERMISSION_POLICY_SCHEMA_VERSION,
  ADMIN_UI_PLUGIN_MANIFEST_SCHEMA_VERSION,
  AdminPluginCompatibilityEvaluator,
  AdminUIPluginManifestValidator,
} from '@prosto/platform-admin-contracts';
import {
  PlatformDelegatedIdentity,
  PlatformHttpRequest,
} from '@prosto/platform-sdk';
import type {
  IPlatformDelegatedIdentity,
  IPlatformHttpRequest,
} from '@prosto/platform-sdk';
import { describe, expect, it, vi } from 'vitest';
import { PlatformAdminBffAdapter } from '@/admin-bff.adapter.js';
import type { IAdminPluginCatalogSource } from '@/admin-bff.interfaces.js';
import type { IAdminBffLogger } from '@/observability/index.js';
import { AdminDiagnosticsService } from '@/diagnostics/index.js';
import { AdminDiscoveryAggregationService } from '@/discovery/index.js';
import { AdminPermissionMappingService } from '@/permissions/index.js';
import {
  AdminPluginAllowlistEvaluator,
  AdminPluginReviewStatusFilter,
  AdminPluginTrustClassFilter,
  type IAdminPluginAllowlistEvaluatorConfig,
  type IAdminPluginReviewStatusPolicyConfig,
  type IAdminPluginTrustClassPolicyConfig,
} from '@/policy/index.js';

const SHELL_VERSION = '1.5.0';
const SUPPORTED_CONTRACT_VERSION = ADMIN_COMPATIBILITY_CONTRACT_VERSION;

function createValidManifest(
  overrides?: Partial<IAdminUIPluginManifest>,
): IAdminUIPluginManifest {
  return {
    schemaVersion: ADMIN_UI_PLUGIN_MANIFEST_SCHEMA_VERSION,
    id: 'catalog-admin-ui',
    version: '1.2.0',
    displayName: 'Catalog Admin UI',
    shellCompatibility: '>=1.0.0',
    requiredPermissions: ['catalog.read'],
    requiredCapabilities: ['catalog'],
    extensionPoints: ['nav', 'page'],
    trustClass: 'trusted',
    reviewStatus: 'approved',
    metadata: { author: 'team-platform' },
    ...overrides,
  };
}

function createCatalogSource(manifests: unknown[]): IAdminPluginCatalogSource {
  return {
    fetchUIPluginManifests: vi.fn().mockResolvedValue(manifests),
  };
}

const DEFAULT_PERMISSION_POLICY: IAdminPermissionPolicy = {
  schemaVersion: ADMIN_PERMISSION_POLICY_SCHEMA_VERSION,
  roleMappings: [
    {
      roleId: 'admin',
      permissions: ['catalog.read', 'catalog.write', 'settings.manage'],
    },
    {
      roleId: 'viewer',
      permissions: ['catalog.read'],
    },
  ],
  actionGates: [
    {
      actionId: 'catalog.export',
      requiredPermissions: ['catalog.read', 'catalog.write'],
      match: 'all',
      effect: 'allow',
    },
  ],
};

function createOperatorIdentity(
  subjectId = 'operator-1',
  roles: string[] = ['admin'],
  permissions: string[] = [],
): IPlatformDelegatedIdentity {
  return new PlatformDelegatedIdentity({ subjectId, roles, permissions });
}

function createSdkRequest(
  overrides?: Partial<IPlatformHttpRequest>,
): IPlatformHttpRequest {
  return new PlatformHttpRequest({
    method: overrides?.method ?? 'GET',
    path: overrides?.path ?? '/admin/api/v1/discovery',
    params: overrides?.params ?? {},
    query: overrides?.query ?? {},
    headers: overrides?.headers ?? { 'user-agent': 'test-agent' },
    body: overrides?.body ?? { variant: 'empty' as const },
    correlationId: overrides?.correlationId ?? 'test-cid',
    identity: overrides?.identity ?? createOperatorIdentity(),
  });
}

function createMockLogger(): IAdminBffLogger & {
  calls: {
    level: string;
    message: string;
    context?: Record<string, unknown>;
  }[];
} {
  const calls: {
    level: string;
    message: string;
    context?: Record<string, unknown>;
  }[] = [];

  return {
    calls,
    debug: vi.fn((message: string, context?: Record<string, unknown>) => {
      calls.push({ level: 'debug', message, context });
    }),
    info: vi.fn((message: string, context?: Record<string, unknown>) => {
      calls.push({ level: 'info', message, context });
    }),
    warn: vi.fn((message: string, context?: Record<string, unknown>) => {
      calls.push({ level: 'warn', message, context });
    }),
    error: vi.fn((message: string, context?: Record<string, unknown>) => {
      calls.push({ level: 'error', message, context });
    }),
  };
}

function buildFullPipeline(
  manifests: unknown[],
  options?: {
    allowlistEntries?: IAdminPluginAllowlistEvaluatorConfig['entries'];
    requireAllowlist?: boolean;
    allowedTrustClasses?: IAdminPluginTrustClassPolicyConfig['allowedTrustClasses'];
    allowedReviewStatuses?: IAdminPluginReviewStatusPolicyConfig['allowedReviewStatuses'];
    permissionPolicy?: IAdminPermissionPolicy;
    logger?: IAdminBffLogger;
  },
) {
  const catalog = createCatalogSource(manifests);
  const validator = new AdminUIPluginManifestValidator();
  const compatibility = new AdminPluginCompatibilityEvaluator();

  const allowlistEvaluator = new AdminPluginAllowlistEvaluator({
    entries: options?.allowlistEntries ?? [
      { pluginIdPattern: 'catalog-admin-ui', versionPattern: '^1.0.0' },
      { pluginIdPattern: '*' },
    ],
    requireAllowlist: options?.requireAllowlist ?? false,
  });

  const trustClassFilter = new AdminPluginTrustClassFilter({
    allowedTrustClasses: options?.allowedTrustClasses ?? [
      'trusted',
      'internal',
    ],
    environment: 'production',
  });

  const reviewStatusFilter = new AdminPluginReviewStatusFilter({
    allowedReviewStatuses: options?.allowedReviewStatuses ?? ['approved'],
  });

  const permissionService = new AdminPermissionMappingService({
    policy: options?.permissionPolicy ?? DEFAULT_PERMISSION_POLICY,
  });

  const discoveryService = new AdminDiscoveryAggregationService(
    catalog,
    validator,
    compatibility,
    {
      shellVersion: SHELL_VERSION,
      supportedContractVersion: SUPPORTED_CONTRACT_VERSION,
    },
    {
      allowlistEvaluator,
      trustClassFilter,
      reviewStatusFilter,
      permissionService,
    },
  );

  const diagnosticsService = new AdminDiagnosticsService({
    environment: 'production',
    shellVersion: SHELL_VERSION,
    discoveryPipelineVersion: 'test-pipeline.v1',
    enableDetailedLogging: true,
  });

  const adapter = new PlatformAdminBffAdapter(
    discoveryService,
    permissionService,
    diagnosticsService,
    { logger: options?.logger },
  );

  return { adapter, catalog, discoveryService, diagnosticsService };
}

describe('Admin BFF observability: adapter request logging', () => {
  it('should log request received and completed for successful requests', async () => {
    const logger = createMockLogger();
    const manifest = createValidManifest();
    const { adapter } = buildFullPipeline([manifest], { logger });

    const request = createSdkRequest();

    const response = await adapter.handleRequest(request);

    expect(response.status).toBe(200);

    const infoCalls = logger.calls.filter(
      (c) => c.level === 'info' && c.context?.phase === 'request',
    );

    expect(infoCalls.length).toBeGreaterThanOrEqual(2);

    const received = infoCalls.find((c) => c.message === 'Request received');

    expect(received).toBeDefined();
    expect(received?.context?.correlationId).toBeDefined();
    expect(received?.context?.method).toBe('GET');
    expect(received?.context?.path).toBe('/admin/api/v1/discovery');
    expect(received?.context?.subjectId).toBeUndefined();
    expect(received?.context?.roles).toBeUndefined();
    expect(received?.context?.permissions).toBeUndefined();

    const completed = infoCalls.find((c) => c.message === 'Request completed');

    expect(completed).toBeDefined();
    expect(completed?.context?.status).toBe(200);
    expect(completed?.context?.duration).toBeGreaterThanOrEqual(0);
  });

  it('should log route not found as warning', async () => {
    const logger = createMockLogger();
    const { adapter } = buildFullPipeline([], { logger });

    const request = new PlatformHttpRequest({
      method: 'GET',
      path: '/unknown/route',
      params: {},
      query: {},
      headers: {},
      body: { variant: 'empty' as const },
      correlationId: 'test-cid',
      identity: createOperatorIdentity(),
    });

    const response = await adapter.handleRequest(request);

    expect(response.status).toBe(404);

    const warnCalls = logger.calls.filter((c) => c.level === 'warn');

    expect(warnCalls.length).toBeGreaterThanOrEqual(1);
    expect(warnCalls[0]?.context?.errorCode).toBe('ADMIN_BFF_ROUTE_NOT_FOUND');
  });

  it('should log request completed with warning status for error responses', async () => {
    const logger = createMockLogger();
    const { adapter } = buildFullPipeline([], { logger });

    const request = createSdkRequest({
      method: 'POST',
      path: '/admin/api/v1/action/nonexistent.action',
      params: { actionId: 'nonexistent.action' },
      identity: createOperatorIdentity('operator-1', ['admin']),
    });

    const response = await adapter.handleRequest(request);

    expect(response.status).toBe(403);

    const warnCalls = logger.calls.filter(
      (c) => c.level === 'warn' && c.context?.phase === 'request',
    );

    expect(warnCalls.length).toBeGreaterThanOrEqual(1);

    const completedWarn = warnCalls.find(
      (c) => c.message === 'Request completed with error status',
    );

    expect(completedWarn).toBeDefined();
    expect(completedWarn?.context?.status).toBe(403);
  });

  it('should log adapter initialization', () => {
    const logger = createMockLogger();
    buildFullPipeline([], { logger });

    const initCalls = logger.calls.filter(
      (c) => c.message === 'Admin BFF adapter initialized',
    );

    expect(initCalls).toHaveLength(1);
    expect(initCalls[0]?.context?.handlerCount).toBe(4);
  });
});

describe('Admin BFF observability: discovery pipeline logging', () => {
  it('should log discovery started and completed events', async () => {
    const logger = createMockLogger();
    const manifest = createValidManifest();
    const { adapter } = buildFullPipeline([manifest], { logger });

    await adapter.handleRequest(createSdkRequest());

    const discoveryStarted = logger.calls.find(
      (c) => c.message === 'Discovery pipeline started',
    );

    expect(discoveryStarted).toBeDefined();
    expect(discoveryStarted?.context?.event).toBe('discovery_started');

    const discoveryCompleted = logger.calls.find(
      (c) => c.message === 'Discovery pipeline completed',
    );

    expect(discoveryCompleted).toBeDefined();
    expect(discoveryCompleted?.context?.event).toBe('discovery_completed');
    expect(discoveryCompleted?.context?.acceptedCount).toBe(1);
    expect(discoveryCompleted?.context?.rejectedCount).toBe(0);
    expect(discoveryCompleted?.context?.duration).toBeGreaterThanOrEqual(0);
  });

  it('should log per-plugin accepted events', async () => {
    const logger = createMockLogger();
    const manifest1 = createValidManifest({ id: 'plugin-a', version: '1.0.0' });
    const manifest2 = createValidManifest({ id: 'plugin-b', version: '2.0.0' });
    const { adapter } = buildFullPipeline([manifest1, manifest2], { logger });

    await adapter.handleRequest(createSdkRequest());

    const acceptedPlugins = logger.calls.filter(
      (c) => c.message === 'Plugin accepted',
    );
    expect(acceptedPlugins).toHaveLength(2);

    const pluginIds = acceptedPlugins.map((c) => c.context?.pluginId);
    expect(pluginIds).toContain('plugin-a');
    expect(pluginIds).toContain('plugin-b');
  });

  it('should log per-plugin rejected events', async () => {
    const logger = createMockLogger();
    const validManifest = createValidManifest();
    const invalidManifest = { id: 'broken-plugin', version: '1.0.0' };
    const { adapter } = buildFullPipeline([validManifest, invalidManifest], {
      logger,
    });

    await adapter.handleRequest(createSdkRequest());

    const rejectedPlugins = logger.calls.filter(
      (c) => c.message === 'Plugin rejected',
    );

    expect(rejectedPlugins).toHaveLength(1);
    expect(rejectedPlugins[0]?.context?.pluginId).toBe('unknown');
    expect(rejectedPlugins[0]?.context?.reasonCode).toBe(
      'MANIFEST_VALIDATION_FAILED',
    );
  });

  it('should log discovery failure with error event', async () => {
    const logger = createMockLogger();
    const catalog = createCatalogSource([]);

    vi.mocked(catalog.fetchUIPluginManifests).mockRejectedValue(
      new Error('Catalog unavailable'),
    );

    const validator = new AdminUIPluginManifestValidator();
    const compatibility = new AdminPluginCompatibilityEvaluator();
    const permissionService = new AdminPermissionMappingService({
      policy: DEFAULT_PERMISSION_POLICY,
    });

    const discoveryService = new AdminDiscoveryAggregationService(
      catalog,
      validator,
      compatibility,
      {
        shellVersion: SHELL_VERSION,
        supportedContractVersion: SUPPORTED_CONTRACT_VERSION,
      },
    );

    const diagnosticsService = new AdminDiagnosticsService({
      environment: 'production',
      shellVersion: SHELL_VERSION,
      discoveryPipelineVersion: 'test-pipeline.v1',
      enableDetailedLogging: true,
    });

    const adapter = new PlatformAdminBffAdapter(
      discoveryService,
      permissionService,
      diagnosticsService,
      { logger },
    );

    await expect(adapter.handleRequest(createSdkRequest())).rejects.toThrow(
      'Catalog unavailable',
    );

    const failedCalls = logger.calls.filter(
      (c) => c.message === 'Discovery pipeline failed',
    );

    expect(failedCalls).toHaveLength(1);
    expect(failedCalls[0]?.context?.event).toBe('discovery_failed');
    expect(failedCalls[0]?.context?.errorCode).toBe(
      'ADMIN_BFF_DISCOVERY_FAILED',
    );
    expect(failedCalls[0]?.context?.error).toBe('Catalog unavailable');
  });
});

describe('Admin BFF observability: action evaluation logging', () => {
  it('should log action allowed event', async () => {
    const logger = createMockLogger();
    const { adapter } = buildFullPipeline([], { logger });

    const request = createSdkRequest({
      method: 'POST',
      path: '/admin/api/v1/action/catalog.export',
      params: { actionId: 'catalog.export' },
      identity: createOperatorIdentity('operator-1', ['admin']),
    });

    await adapter.handleRequest(request);

    const allowedCalls = logger.calls.filter(
      (c) => c.message === 'Action allowed',
    );

    expect(allowedCalls).toHaveLength(1);
    expect(allowedCalls[0]?.context?.event).toBe('action_evaluated');
    expect(allowedCalls[0]?.context?.actionId).toBe('catalog.export');
    expect(allowedCalls[0]?.context?.allowed).toBe(true);
  });

  it('should log action denied event with warning', async () => {
    const logger = createMockLogger();
    const { adapter } = buildFullPipeline([], { logger });

    const request = createSdkRequest({
      method: 'POST',
      path: '/admin/api/v1/action/catalog.export',
      params: { actionId: 'catalog.export' },
      identity: createOperatorIdentity('viewer-operator', ['viewer']),
    });

    await adapter.handleRequest(request);

    const deniedCalls = logger.calls.filter(
      (c) => c.message === 'Action denied',
    );

    expect(deniedCalls).toHaveLength(1);
    expect(deniedCalls[0]?.context?.event).toBe('action_evaluated');
    expect(deniedCalls[0]?.context?.allowed).toBe(false);
    expect(deniedCalls[0]?.context?.reasonCode).toBe(
      'PERMISSION_REQUIREMENT_NOT_MET',
    );
    expect(deniedCalls[0]?.context?.errorCode).toBe(
      'ADMIN_BFF_PERMISSION_DENIED',
    );
  });

  it('should log warning when actionId is missing', async () => {
    const logger = createMockLogger();
    const { adapter } = buildFullPipeline([], { logger });

    const request = new PlatformHttpRequest({
      method: 'POST',
      path: '/admin/api/v1/action/',
      params: {},
      query: {},
      headers: {},
      body: { variant: 'empty' as const },
      correlationId: 'test-cid',
      identity: createOperatorIdentity(),
    });

    await adapter.handleRequest(request);

    const warnCalls = logger.calls.filter(
      (c) => c.message === 'Action evaluation requested without actionId',
    );

    expect(warnCalls).toHaveLength(1);
    expect(warnCalls[0]?.context?.errorCode).toBe(
      'ADMIN_BFF_VALIDATION_FAILED',
    );
  });
});

describe('Admin BFF observability: health check logging', () => {
  it('should log health check started and completed', async () => {
    const logger = createMockLogger();
    const manifest = createValidManifest();
    const { adapter } = buildFullPipeline([manifest], { logger });

    await adapter.handleRequest(
      createSdkRequest({ path: '/admin/api/v1/health' }),
    );

    const started = logger.calls.find(
      (c) => c.message === 'Health check started',
    );
    expect(started).toBeDefined();
    expect(started?.context?.phase).toBe('health_check');

    const completed = logger.calls.find(
      (c) => c.message === 'Health check completed',
    );

    expect(completed).toBeDefined();
    expect(completed?.context?.event).toBe('health_check_result');
    expect(completed?.context?.status).toBe('healthy');
    expect(completed?.context?.acceptedPlugins).toBe(1);
    expect(completed?.context?.rejectedPlugins).toBe(0);
  });

  it('should log degraded health status when plugins rejected', async () => {
    const logger = createMockLogger();
    const manifest = createValidManifest();
    const catalog = createCatalogSource([manifest]);
    const validator = new AdminUIPluginManifestValidator();
    const compatibility = new AdminPluginCompatibilityEvaluator();
    const allowlistEvaluator = new AdminPluginAllowlistEvaluator({
      entries: [{ pluginIdPattern: 'nonexistent' }],
      requireAllowlist: true,
    });

    const discoveryService = new AdminDiscoveryAggregationService(
      catalog,
      validator,
      compatibility,
      {
        shellVersion: SHELL_VERSION,
        supportedContractVersion: SUPPORTED_CONTRACT_VERSION,
      },
      { allowlistEvaluator },
    );

    const diagnosticsService = new AdminDiagnosticsService({
      environment: 'production',
      shellVersion: SHELL_VERSION,
      discoveryPipelineVersion: 'test-pipeline.v1',
      enableDetailedLogging: true,
    });

    const permissionService = new AdminPermissionMappingService({
      policy: DEFAULT_PERMISSION_POLICY,
    });

    const adapter = new PlatformAdminBffAdapter(
      discoveryService,
      permissionService,
      diagnosticsService,
      { logger },
    );

    await adapter.handleRequest(
      createSdkRequest({ path: '/admin/api/v1/health' }),
    );

    const completed = logger.calls.find(
      (c) => c.message === 'Health check completed',
    );

    expect(completed).toBeDefined();
    expect(completed?.context?.status).toBe('degraded');
    expect(completed?.context?.rejectedPlugins).toBe(1);
  });
});

describe('Admin BFF observability: diagnostics logging', () => {
  it('should log diagnostics generation started and completed', async () => {
    const logger = createMockLogger();
    const manifest = createValidManifest();
    const { adapter } = buildFullPipeline([manifest], { logger });

    await adapter.handleRequest(
      createSdkRequest({ path: '/admin/api/v1/diagnostics' }),
    );

    const started = logger.calls.find(
      (c) => c.message === 'Diagnostics generation started',
    );
    expect(started).toBeDefined();
    expect(started?.context?.phase).toBe('diagnostics');

    const completed = logger.calls.find(
      (c) => c.message === 'Diagnostics generated',
    );

    expect(completed).toBeDefined();
    expect(completed?.context?.event).toBe('diagnostics_generated');
    expect(completed?.context?.acceptedCount).toBe(1);
    expect(completed?.context?.rejectedCount).toBe(0);
    expect(completed?.context?.duration).toBeGreaterThanOrEqual(0);
  });
});

describe('Admin BFF observability: correlation ID propagation', () => {
  it('should use provided correlation ID', async () => {
    const logger = createMockLogger();
    const manifest = createValidManifest();
    const { adapter } = buildFullPipeline([manifest], { logger });

    const request = createSdkRequest({ correlationId: 'custom-corr-id' });

    const response = await adapter.handleRequest(request);

    expect(response.status).toBe(200);

    const body = response.body.data as { correlationId: string };
    expect(body.correlationId).toBe('custom-corr-id');

    const received = logger.calls.find((c) => c.message === 'Request received');

    expect(received?.context?.correlationId).toBe('custom-corr-id');
  });

  it('should generate correlation ID when not provided', async () => {
    const logger = createMockLogger();
    const manifest = createValidManifest();
    const { adapter } = buildFullPipeline([manifest], { logger });

    const request = new PlatformHttpRequest({
      method: 'GET',
      path: '/admin/api/v1/discovery',
      params: {},
      query: {},
      headers: { 'user-agent': 'test-agent' },
      body: { variant: 'empty' as const },
      identity: createOperatorIdentity(),
    });

    const response = await adapter.handleRequest(request);

    const body = response.body.data as { correlationId: string };

    expect(body.correlationId).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/,
    );

    const received = logger.calls.find((c) => c.message === 'Request received');

    expect(received?.context?.correlationId).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/,
    );
  });

  it('should propagate correlation ID across all log entries for a request', async () => {
    const logger = createMockLogger();
    const manifest = createValidManifest();
    const { adapter } = buildFullPipeline([manifest], { logger });

    await adapter.handleRequest(
      createSdkRequest({ correlationId: 'trace-001' }),
    );

    const allCalls = logger.calls.filter(
      (c) =>
        c.context?.correlationId === 'trace-001' || c.context?.phase === 'init',
    );

    expect(allCalls.length).toBeGreaterThanOrEqual(4);

    for (const call of allCalls) {
      if (call.context?.phase !== 'init') {
        expect(call.context?.correlationId).toBe('trace-001');
      }
    }
  });
});

describe('Admin BFF observability: identity privacy', () => {
  it('should not provide identity PII to a custom logger', async () => {
    const logger = createMockLogger();
    const { adapter } = buildFullPipeline([createValidManifest()], { logger });

    await adapter.handleRequest(createSdkRequest());
    await adapter.handleRequest(
      createSdkRequest({ path: '/admin/api/v1/health' }),
    );
    await adapter.handleRequest(
      createSdkRequest({ path: '/admin/api/v1/diagnostics' }),
    );
    await adapter.handleRequest(
      createSdkRequest({
        method: 'POST',
        path: '/admin/api/v1/action/catalog.export',
        params: { actionId: 'catalog.export' },
      }),
    );

    for (const call of logger.calls) {
      expect(call.context).not.toHaveProperty('subject');
      expect(call.context).not.toHaveProperty('subjectId');
      expect(call.context).not.toHaveProperty('roles');
      expect(call.context).not.toHaveProperty('permissions');
    }
  });
});
