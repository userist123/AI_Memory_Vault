import type { IAdminDiscoveredPluginDescriptor } from '@prosto/platform-admin-contracts';
import type {
  IAdminDiagnosticsRequestContext,
  IAdminDiagnosticsService,
} from '@/diagnostics/index.js';
import type {
  IAdminActionEvaluationResult,
  IAdminDiscoveryAggregationService,
  IAdminDiscoveryResult,
  IAdminPermissionMappingService,
} from '@/admin-bff.interfaces.js';
import type { IPlatformDelegatedIdentity } from '@prosto/platform-sdk';
import { describe, expect, it, vi } from 'vitest';
import { PlatformAdminBffAdapter } from '@/admin-bff.adapter.js';
import {
  AdminActionRouteHandler,
  AdminDiagnosticsRouteHandler,
  AdminDiscoveryRouteHandler,
  AdminHealthRouteHandler,
} from '@/routes/index.js';

function createMockDiscoveryResult(
  overrides?: Partial<IAdminDiscoveryResult>,
): IAdminDiscoveryResult {
  return {
    payload: {
      schemaVersion: 'admin-discovery-payload.v1',
      generatedAt: new Date().toISOString(),
      plugins: [],
      rejected: [],
    },
    diagnostics: {
      acceptedCount: 0,
      rejectedCount: 0,
      duration: 10,
    },
    ...overrides,
  };
}

function createMockDiscoveryService(
  result?: IAdminDiscoveryResult,
): IAdminDiscoveryAggregationService {
  return {
    discover: vi.fn().mockResolvedValue(result ?? createMockDiscoveryResult()),
  };
}

function createMockPlugin(
  overrides?: Partial<IAdminDiscoveredPluginDescriptor>,
): IAdminDiscoveredPluginDescriptor {
  return {
    id: 'mock-plugin',
    version: '1.0.0',
    shellCompatibility: '*',
    trustClass: 'trusted',
    reviewStatus: 'approved',
    extensions: { navigation: [], pages: [], widgets: [], actions: [] },
    ...overrides,
  };
}

function createMockPermissionService(
  evaluation?: IAdminActionEvaluationResult,
): IAdminPermissionMappingService {
  return {
    evaluateAction: vi.fn().mockReturnValue(
      evaluation ?? {
        allowed: true,
        actionId: 'test-action',
      },
    ),
    filterPermissions: vi.fn().mockReturnValue({
      allowed: true,
      missingPermissions: [],
    }),
  };
}

function createMockDiagnosticsService(): IAdminDiagnosticsService {
  return {
    generateDiagnosticsPayload: vi
      .fn()
      .mockImplementation(
        (
          result: IAdminDiscoveryResult,
          ctx: IAdminDiagnosticsRequestContext,
        ) => ({
          schemaVersion: 'admin-diagnostics.v1',
          generatedAt: new Date().toISOString(),
          correlationId: ctx.correlationId,
          plugins: [
            ...result.payload.plugins.map((p) => ({
              pluginId: p.id,
              pluginVersion: p.version,
              status: 'accepted' as const,
              timestamp: new Date().toISOString(),
              correlationId: ctx.correlationId,
              subjectId: ctx.identity.subjectId,
            })),
            ...result.payload.rejected.map((r) => ({
              pluginId: r.id ?? 'unknown',
              pluginVersion: r.version,
              status: 'rejected' as const,
              reasonCode: r.reasonCode,
              message: r.message,
              remediationHint: r.remediationHint,
              timestamp: new Date().toISOString(),
              correlationId: ctx.correlationId,
              subjectId: ctx.identity.subjectId,
            })),
          ],
          summary: {
            acceptedCount: result.diagnostics.acceptedCount,
            rejectedCount: result.diagnostics.rejectedCount,
            filteredCount: 0,
            totalCount:
              result.diagnostics.acceptedCount +
              result.diagnostics.rejectedCount,
            duration: result.diagnostics.duration,
            timestamp: new Date().toISOString(),
            correlationId: ctx.correlationId,
          },
          metadata: {
            subjectId: ctx.identity.subjectId,
            roles: [...ctx.identity.roles],
            requestPath: ctx.requestPath,
            discoveryPipelineVersion: '1.0.0',
          },
        }),
      ),
    createPluginEntry: vi.fn(),
    aggregateDiagnostics: vi.fn(),
  };
}

function createMockDelegatedIdentity(): IPlatformDelegatedIdentity {
  return {
    authenticationType: 'delegated',
    subjectId: 'operator-1',
    roles: ['admin'],
    permissions: ['read', 'write'],
  };
}

function createMockLogger() {
  return {
    debug: vi.fn(),
    info: vi.fn(),
    warn: vi.fn(),
    error: vi.fn(),
  };
}

describe('PlatformAdminBffAdapter', () => {
  it('should create adapter with route handlers', () => {
    const adapter = new PlatformAdminBffAdapter(
      createMockDiscoveryService(),
      createMockPermissionService(),
      createMockDiagnosticsService(),
    );

    const handlers = adapter.getHandlers();
    expect(handlers).toHaveLength(4);
  });

  it('should find discovery handler for GET /admin/api/v1/discovery', () => {
    const adapter = new PlatformAdminBffAdapter(
      createMockDiscoveryService(),
      createMockPermissionService(),
      createMockDiagnosticsService(),
    );

    const handler = adapter.findHandler('GET', '/admin/api/v1/discovery');
    expect(handler).toBeInstanceOf(AdminDiscoveryRouteHandler);
  });

  it('should find action handler for POST /admin/api/v1/action/:actionId', () => {
    const adapter = new PlatformAdminBffAdapter(
      createMockDiscoveryService(),
      createMockPermissionService(),
      createMockDiagnosticsService(),
    );

    const handler = adapter.findHandler(
      'POST',
      '/admin/api/v1/action/my-action',
    );
    expect(handler).toBeInstanceOf(AdminActionRouteHandler);
  });

  it('should find health handler for GET /admin/api/v1/health', () => {
    const adapter = new PlatformAdminBffAdapter(
      createMockDiscoveryService(),
      createMockPermissionService(),
      createMockDiagnosticsService(),
    );

    const handler = adapter.findHandler('GET', '/admin/api/v1/health');
    expect(handler).toBeInstanceOf(AdminHealthRouteHandler);
  });

  it('should find diagnostics handler for GET /admin/api/v1/diagnostics', () => {
    const adapter = new PlatformAdminBffAdapter(
      createMockDiscoveryService(),
      createMockPermissionService(),
      createMockDiagnosticsService(),
    );

    const handler = adapter.findHandler('GET', '/admin/api/v1/diagnostics');
    expect(handler).toBeInstanceOf(AdminDiagnosticsRouteHandler);
  });

  it('should return undefined for unknown route', () => {
    const adapter = new PlatformAdminBffAdapter(
      createMockDiscoveryService(),
      createMockPermissionService(),
      createMockDiagnosticsService(),
    );

    const handler = adapter.findHandler('GET', '/unknown/route');
    expect(handler).toBeUndefined();
  });

  it('should return 404 for unmatched route via handleRequest', async () => {
    const adapter = new PlatformAdminBffAdapter(
      createMockDiscoveryService(),
      createMockPermissionService(),
      createMockDiagnosticsService(),
    );

    const response = await adapter.handleRequest({
      method: 'GET',
      path: '/unknown',
      params: {},
      query: {},
      body: null,
      headers: {},
      correlationId: 'test-correlation',
      identity: createMockDelegatedIdentity(),
    });

    expect(response.status).toBe(404);
    expect(response.body.data).toMatchObject({
      error: {
        code: 'ROUTE_NOT_FOUND',
      },
    });
  });

  it('should dispatch discovery request successfully', async () => {
    const discoveryResult = createMockDiscoveryResult({
      payload: {
        schemaVersion: 'admin-discovery-payload.v1',
        generatedAt: new Date().toISOString(),
        plugins: [createMockPlugin({ id: 'test-plugin' })],
        rejected: [],
      },
      diagnostics: {
        acceptedCount: 1,
        rejectedCount: 0,
        duration: 5,
      },
    });

    const adapter = new PlatformAdminBffAdapter(
      createMockDiscoveryService(discoveryResult),
      createMockPermissionService(),
      createMockDiagnosticsService(),
    );

    const response = await adapter.handleRequest({
      method: 'GET',
      path: '/admin/api/v1/discovery',
      params: {},
      query: {},
      body: null,
      headers: {},
      correlationId: 'test-correlation',
      identity: createMockDelegatedIdentity(),
    });

    expect(response.status).toBe(200);
    expect(response.body.data).toMatchObject({
      data: {
        plugins: [{ id: 'test-plugin' }],
      },
    });
  });
});

describe('AdminDiscoveryRouteHandler', () => {
  it('should return discovery payload with 200 status', async () => {
    const handler = new AdminDiscoveryRouteHandler();
    const discoveryResult = createMockDiscoveryResult();

    const response = await handler.handle(
      {
        method: 'GET',
        path: '/admin/api/v1/discovery',
        params: {},
        query: {},
        body: null,
        headers: {},
      },
      {
        correlationId: 'test-correlation',
        identity: createMockDelegatedIdentity(),
        signal: new AbortController().signal,
        discoveryService: createMockDiscoveryService(discoveryResult),
        permissionService: createMockPermissionService(),
        diagnosticsService: createMockDiagnosticsService(),
        logger: createMockLogger(),
      },
    );

    expect(response.status).toBe(200);
    expect(response.body.data).toMatchObject({
      correlationId: 'test-correlation',
    });
  });
});

describe('AdminActionRouteHandler', () => {
  it('should return 400 when actionId is missing', async () => {
    const handler = new AdminActionRouteHandler();

    const response = await handler.handle(
      {
        method: 'POST',
        path: '/admin/api/v1/action/',
        params: {},
        query: {},
        body: null,
        headers: {},
      },
      {
        correlationId: 'test-correlation',
        identity: createMockDelegatedIdentity(),
        signal: new AbortController().signal,
        discoveryService: createMockDiscoveryService(),
        permissionService: createMockPermissionService(),
        diagnosticsService: createMockDiagnosticsService(),
        logger: createMockLogger(),
      },
    );

    expect(response.status).toBe(400);
    expect(response.body.data).toMatchObject({
      error: {
        code: 'MISSING_ACTION_ID',
      },
    });
  });

  it('should return 200 when action is allowed', async () => {
    const handler = new AdminActionRouteHandler();

    const response = await handler.handle(
      {
        method: 'POST',
        path: '/admin/api/v1/action/test-action',
        params: { actionId: 'test-action' },
        query: {},
        body: null,
        headers: {},
      },
      {
        correlationId: 'test-correlation',
        identity: createMockDelegatedIdentity(),
        signal: new AbortController().signal,
        discoveryService: createMockDiscoveryService(),
        permissionService: createMockPermissionService(),
        diagnosticsService: createMockDiagnosticsService(),
        logger: createMockLogger(),
      },
    );

    expect(response.status).toBe(200);
    expect(response.body.data).toMatchObject({
      data: {
        actionId: 'test-action',
        allowed: true,
      },
    });
  });

  it('should return 403 when action is denied', async () => {
    const handler = new AdminActionRouteHandler();
    const deniedEvaluation: IAdminActionEvaluationResult = {
      allowed: false,
      actionId: 'test-action',
      reasonCode: 'PERMISSION_REQUIREMENT_NOT_MET',
      remediationHint: 'Request admin role.',
    };

    const response = await handler.handle(
      {
        method: 'POST',
        path: '/admin/api/v1/action/test-action',
        params: { actionId: 'test-action' },
        query: {},
        body: null,
        headers: {},
      },
      {
        correlationId: 'test-correlation',
        identity: createMockDelegatedIdentity(),
        signal: new AbortController().signal,
        discoveryService: createMockDiscoveryService(),
        permissionService: createMockPermissionService(deniedEvaluation),
        diagnosticsService: createMockDiagnosticsService(),
        logger: createMockLogger(),
      },
    );

    expect(response.status).toBe(403);
    expect(response.body.data).toMatchObject({
      error: {
        code: 'PERMISSION_REQUIREMENT_NOT_MET',
        remediationHint: 'Request admin role.',
      },
    });
  });
});

describe('AdminHealthRouteHandler', () => {
  it('should return healthy status when no rejections', async () => {
    const handler = new AdminHealthRouteHandler();
    const discoveryResult = createMockDiscoveryResult({
      diagnostics: { acceptedCount: 3, rejectedCount: 0, duration: 15 },
    });

    const response = await handler.handle(
      {
        method: 'GET',
        path: '/admin/api/v1/health',
        params: {},
        query: {},
        body: null,
        headers: {},
      },
      {
        correlationId: 'test-correlation',
        identity: createMockDelegatedIdentity(),
        signal: new AbortController().signal,
        discoveryService: createMockDiscoveryService(discoveryResult),
        permissionService: createMockPermissionService(),
        diagnosticsService: createMockDiagnosticsService(),
        logger: createMockLogger(),
      },
    );

    expect(response.status).toBe(200);
    expect(response.body.data).toMatchObject({
      status: 'healthy',
      adapter: 'platform-adapter-admin-bff',
    });
  });

  it('should return degraded status when plugins are rejected', async () => {
    const handler = new AdminHealthRouteHandler();
    const discoveryResult = createMockDiscoveryResult({
      diagnostics: { acceptedCount: 2, rejectedCount: 1, duration: 20 },
    });

    const response = await handler.handle(
      {
        method: 'GET',
        path: '/admin/api/v1/health',
        params: {},
        query: {},
        body: null,
        headers: {},
      },
      {
        correlationId: 'test-correlation',
        identity: createMockDelegatedIdentity(),
        signal: new AbortController().signal,
        discoveryService: createMockDiscoveryService(discoveryResult),
        permissionService: createMockPermissionService(),
        diagnosticsService: createMockDiagnosticsService(),
        logger: createMockLogger(),
      },
    );

    expect(response.status).toBe(200);
    expect(response.body.data).toMatchObject({
      status: 'degraded',
    });
  });
});

describe('AdminDiagnosticsRouteHandler', () => {
  it('should return full diagnostics payload', async () => {
    const handler = new AdminDiagnosticsRouteHandler();
    const discoveryResult = createMockDiscoveryResult({
      payload: {
        schemaVersion: 'admin-discovery-payload.v1',
        generatedAt: new Date().toISOString(),
        plugins: [createMockPlugin({ id: 'plugin-a', version: '1.0.0' })],
        rejected: [
          {
            id: 'plugin-b',
            version: '2.0.0',
            reasonCode: 'ALLOWLIST_REJECTED',
            message: 'Not allowed',
            remediationHint: 'Add to allowlist',
          },
        ],
      },
      diagnostics: { acceptedCount: 1, rejectedCount: 1, duration: 12 },
    });

    const response = await handler.handle(
      {
        method: 'GET',
        path: '/admin/api/v1/diagnostics',
        params: {},
        query: {},
        body: null,
        headers: {},
      },
      {
        correlationId: 'test-correlation',
        identity: createMockDelegatedIdentity(),
        signal: new AbortController().signal,
        discoveryService: createMockDiscoveryService(discoveryResult),
        permissionService: createMockPermissionService(),
        diagnosticsService: createMockDiagnosticsService(),
        logger: createMockLogger(),
      },
    );

    expect(response.status).toBe(200);
    expect(response.body.data).toMatchObject({
      schemaVersion: 'admin-diagnostics.v1',
      correlationId: 'test-correlation',
      plugins: [
        { pluginId: 'plugin-a', status: 'accepted' },
        {
          pluginId: 'plugin-b',
          status: 'rejected',
          reasonCode: 'ALLOWLIST_REJECTED',
        },
      ],
      summary: {
        acceptedCount: 1,
        rejectedCount: 1,
      },
    });
  });
});
