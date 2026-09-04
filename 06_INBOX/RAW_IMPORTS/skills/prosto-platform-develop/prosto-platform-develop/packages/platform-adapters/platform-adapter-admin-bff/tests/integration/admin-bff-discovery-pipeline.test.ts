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
import type { IAdminPluginCatalogSource } from '@/admin-bff.interfaces.js';
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
import { PlatformAdminBffAdapter } from '@/admin-bff.adapter.js';

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
    {
      roleId: 'operator',
      permissions: ['catalog.read', 'catalog.write'],
    },
  ],
  actionGates: [
    {
      actionId: 'catalog.export',
      requiredPermissions: ['catalog.read', 'catalog.write'],
      match: 'all',
      effect: 'allow',
    },
    {
      actionId: 'settings.reset',
      requiredPermissions: ['settings.manage'],
      match: 'all',
      effect: 'allow',
      remediationHint:
        'Request settings.manage permission from an administrator.',
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

function buildFullPipeline(
  manifests: unknown[],
  options?: {
    allowlistEntries?: IAdminPluginAllowlistEvaluatorConfig['entries'];
    requireAllowlist?: boolean;
    allowedTrustClasses?: IAdminPluginTrustClassPolicyConfig['allowedTrustClasses'];
    allowedReviewStatuses?: IAdminPluginReviewStatusPolicyConfig['allowedReviewStatuses'];
    permissionPolicy?: IAdminPermissionPolicy;
  },
) {
  const catalog = createCatalogSource(manifests);
  const validator = new AdminUIPluginManifestValidator();
  const compatibility = new AdminPluginCompatibilityEvaluator();

  const allowlistEvaluator = new AdminPluginAllowlistEvaluator({
    entries: options?.allowlistEntries ?? [
      { pluginIdPattern: 'catalog-admin-ui', versionPattern: '^1.0.0' },
      { pluginIdPattern: 'settings-panel' },
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
  );

  return { adapter, catalog, discoveryService, diagnosticsService };
}

describe('Admin BFF integration: compliant plugin discovery', () => {
  it('should discover a single compliant plugin through the full pipeline', async () => {
    const manifest = createValidManifest();
    const { adapter } = buildFullPipeline([manifest]);

    const request = createSdkRequest();

    const response = await adapter.handleRequest(request);

    expect(response.status).toBe(200);

    const body = response.body.data as {
      correlationId: string;
      data: {
        schemaVersion: string;
        plugins: {
          id: string;
          version: string;
          trustClass: string;
          reviewStatus: string;
          extensions: unknown;
        }[];
        rejected: unknown[];
      };
      diagnostics: {
        acceptedCount: number;
        rejectedCount: number;
      };
    };

    expect(body.data.schemaVersion).toBe('admin-discovery-payload.v1');
    expect(body.data.plugins).toHaveLength(1);
    expect(body.data.rejected).toHaveLength(0);

    const plugin = body.data.plugins[0];

    expect(plugin?.id).toBe('catalog-admin-ui');
    expect(plugin?.version).toBe('1.2.0');
    expect(plugin?.trustClass).toBe('trusted');
    expect(plugin?.reviewStatus).toBe('approved');
    expect(plugin?.extensions).toEqual({
      navigation: [],
      pages: [],
      widgets: [],
      actions: [],
    });

    expect(body.diagnostics.acceptedCount).toBe(1);
    expect(body.diagnostics.rejectedCount).toBe(0);
    expect(body.correlationId).toBeDefined();
  });

  it('should discover multiple compliant plugins from catalog', async () => {
    const manifest1 = createValidManifest({
      id: 'catalog-admin-ui',
      version: '1.2.0',
      displayName: 'Catalog Admin UI',
    });
    const manifest2 = createValidManifest({
      id: 'settings-panel',
      version: '2.0.0',
      displayName: 'Settings Panel',
      shellCompatibility: '>=1.0.0',
      requiredPermissions: ['settings.manage'],
      requiredCapabilities: ['settings'],
      extensionPoints: ['nav', 'widget'],
    });

    const { adapter } = buildFullPipeline([manifest1, manifest2]);
    const request = createSdkRequest();

    const response = await adapter.handleRequest(request);
    const body = response.body.data as {
      data: {
        plugins: { id: string; version: string }[];
        rejected: unknown[];
      };
    };

    expect(response.status).toBe(200);
    expect(body.data.plugins).toHaveLength(2);
    expect(body.data.rejected).toHaveLength(0);

    const ids = body.data.plugins.map((p) => p.id);

    expect(ids).toContain('catalog-admin-ui');
    expect(ids).toContain('settings-panel');
  });

  it('should return valid discovery payload schema version', async () => {
    const manifest = createValidManifest();
    const { adapter } = buildFullPipeline([manifest]);

    const request = createSdkRequest();

    const response = await adapter.handleRequest(request);
    const body = response.body.data as {
      data: { schemaVersion: string; generatedAt: string };
    };

    expect(body.data.schemaVersion).toBe('admin-discovery-payload.v1');
    expect(body.data.generatedAt).toBeDefined();
    expect(new Date(body.data.generatedAt).getTime()).not.toBeNaN();
  });

  it('should handle empty catalog and return empty plugins list', async () => {
    const { adapter } = buildFullPipeline([]);

    const request = createSdkRequest();

    const response = await adapter.handleRequest(request);
    const body = response.body.data as {
      data: {
        plugins: unknown[];
        rejected: unknown[];
      };
      diagnostics: {
        acceptedCount: number;
        rejectedCount: number;
      };
    };

    expect(response.status).toBe(200);
    expect(body.data.plugins).toHaveLength(0);
    expect(body.data.rejected).toHaveLength(0);
    expect(body.diagnostics.acceptedCount).toBe(0);
    expect(body.diagnostics.rejectedCount).toBe(0);
  });
});

describe('Admin BFF integration: rejected plugin diagnostics', () => {
  it('should reject manifest failing schema validation and include diagnostics', async () => {
    const validManifest = createValidManifest();
    const invalidManifest = { id: 'broken-plugin', version: '1.0.0' };

    const { adapter } = buildFullPipeline([validManifest, invalidManifest]);

    const request = createSdkRequest();

    const response = await adapter.handleRequest(request);
    const body = response.body.data as {
      data: {
        plugins: { id: string }[];
        rejected: {
          reasonCode: string;
          message: string;
          remediationHint: string;
        }[];
      };
      diagnostics: {
        acceptedCount: number;
        rejectedCount: number;
      };
    };

    expect(response.status).toBe(200);
    expect(body.data.plugins).toHaveLength(1);
    expect(body.data.plugins[0]?.id).toBe('catalog-admin-ui');
    expect(body.data.rejected).toHaveLength(1);
    expect(body.data.rejected[0]?.reasonCode).toBe(
      'MANIFEST_VALIDATION_FAILED',
    );
    expect(body.data.rejected[0]?.message).toBeDefined();
    expect(body.data.rejected[0]?.remediationHint).toBe(
      'Fix manifest validation errors and republish.',
    );
    expect(body.diagnostics.acceptedCount).toBe(1);
    expect(body.diagnostics.rejectedCount).toBe(1);
  });

  it('should reject plugins failing shell compatibility check', async () => {
    const manifest = createValidManifest({
      shellCompatibility: '>=5.0.0',
    });

    const { adapter } = buildFullPipeline([manifest]);

    const request = createSdkRequest();

    const response = await adapter.handleRequest(request);
    const body = response.body.data as {
      data: {
        plugins: unknown[];
        rejected: {
          id: string;
          version: string;
          reasonCode: string;
          message: string;
          remediationHint: string;
        }[];
      };
      diagnostics: {
        acceptedCount: number;
        rejectedCount: number;
      };
    };

    expect(response.status).toBe(200);
    expect(body.data.plugins).toHaveLength(0);
    expect(body.data.rejected).toHaveLength(1);
    expect(body.data.rejected[0]?.id).toBe('catalog-admin-ui');
    expect(body.data.rejected[0]?.reasonCode).toBe('SHELL_VERSION_MISMATCH');
    expect(body.data.rejected[0]?.remediationHint).toContain('compatible');
    expect(body.diagnostics.acceptedCount).toBe(0);
    expect(body.diagnostics.rejectedCount).toBe(1);
  });

  it('should reject plugins with disallowed trust class', async () => {
    const manifest = createValidManifest({
      trustClass: 'third-party-reviewed',
    });

    const { adapter } = buildFullPipeline([manifest], {
      allowedTrustClasses: ['trusted', 'internal'],
    });

    const request = createSdkRequest();

    const response = await adapter.handleRequest(request);
    const body = response.body.data as {
      data: {
        plugins: unknown[];
        rejected: {
          id: string;
          reasonCode: string;
          message: string;
          remediationHint: string;
        }[];
      };
    };

    expect(response.status).toBe(200);
    expect(body.data.plugins).toHaveLength(0);
    expect(body.data.rejected).toHaveLength(1);
    expect(body.data.rejected[0]?.reasonCode).toBe('TRUST_CLASS_REJECTED');
    expect(body.data.rejected[0]?.id).toBe('catalog-admin-ui');
    expect(body.data.rejected[0]?.message).toContain('third-party-reviewed');
  });

  it('should reject plugins with non-approved review status', async () => {
    const manifest = createValidManifest({
      reviewStatus: 'pending',
    });

    const { adapter } = buildFullPipeline([manifest], {
      allowedReviewStatuses: ['approved'],
    });

    const request = createSdkRequest();

    const response = await adapter.handleRequest(request);
    const body = response.body.data as {
      data: {
        plugins: unknown[];
        rejected: {
          id: string;
          reasonCode: string;
          message: string;
          remediationHint: string;
        }[];
      };
    };

    expect(response.status).toBe(200);
    expect(body.data.plugins).toHaveLength(0);
    expect(body.data.rejected).toHaveLength(1);
    expect(body.data.rejected[0]?.reasonCode).toBe('REVIEW_STATUS_REJECTED');
    expect(body.data.rejected[0]?.id).toBe('catalog-admin-ui');
  });

  it('should reject plugins not on allowlist when requireAllowlist is true', async () => {
    const manifest = createValidManifest({ id: 'unlisted-plugin' });

    const { adapter } = buildFullPipeline([manifest], {
      requireAllowlist: true,
      allowlistEntries: [
        { pluginIdPattern: 'catalog-admin-ui', versionPattern: '^1.0.0' },
      ],
    });

    const request = createSdkRequest();

    const response = await adapter.handleRequest(request);
    const body = response.body.data as {
      data: {
        plugins: unknown[];
        rejected: {
          id: string;
          reasonCode: string;
          message: string;
          remediationHint: string;
        }[];
      };
    };

    expect(response.status).toBe(200);
    expect(body.data.plugins).toHaveLength(0);
    expect(body.data.rejected).toHaveLength(1);
    expect(body.data.rejected[0]?.reasonCode).toBe('ALLOWLIST_REJECTED');
    expect(body.data.rejected[0]?.id).toBe('unlisted-plugin');
    expect(body.data.rejected[0]?.remediationHint).toContain('allowlist');
  });

  it('should produce structured diagnostics payload via diagnostics route', async () => {
    const validManifest = createValidManifest();
    const invalidManifest = { id: 'broken-plugin' };

    const { adapter } = buildFullPipeline([validManifest, invalidManifest]);
    const request = createSdkRequest({ path: '/admin/api/v1/diagnostics' });

    const response = await adapter.handleRequest(request);
    const body = response.body.data as {
      schemaVersion: string;
      correlationId: string;
      environment: string;
      shellVersion: string;
      plugins: {
        pluginId: string;
        status: string;
        reasonCode?: string;
        remediationHint?: string;
        correlationId: string;
        subjectId: string;
      }[];
      summary: {
        acceptedCount: number;
        rejectedCount: number;
        totalCount: number;
      };
      metadata: {
        subjectId: string;
        roles: string[];
        discoveryPipelineVersion: string;
      };
    };

    expect(response.status).toBe(200);
    expect(body.schemaVersion).toBe('admin-diagnostics.v1');
    expect(body.correlationId).toBeDefined();
    expect(body.environment).toBe('production');
    expect(body.shellVersion).toBe(SHELL_VERSION);

    expect(body.plugins).toHaveLength(2);

    const accepted = body.plugins.filter((p) => p.status === 'accepted');
    const rejected = body.plugins.filter((p) => p.status === 'rejected');

    expect(accepted).toHaveLength(1);
    expect(rejected).toHaveLength(1);
    expect(accepted[0]?.pluginId).toBe('catalog-admin-ui');
    expect(rejected[0]?.reasonCode).toBe('MANIFEST_VALIDATION_FAILED');
    expect(rejected[0]?.remediationHint).toBeDefined();

    expect(body.summary.acceptedCount).toBe(1);
    expect(body.summary.rejectedCount).toBe(1);
    expect(body.summary.totalCount).toBe(2);

    expect(body.metadata.subjectId).toBe('operator-1');
    expect(body.metadata.roles).toEqual(['admin']);
    expect(body.metadata.discoveryPipelineVersion).toBe('test-pipeline.v1');
  });

  it('should return 404 for unknown route', async () => {
    const { adapter } = buildFullPipeline([]);
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

    const body = response.body.data as { error: { code: string } };

    expect(body.error.code).toBe('ROUTE_NOT_FOUND');
  });
});

describe('Admin BFF integration: role-based filtering outcomes', () => {
  it('should allow admin operator to access all plugins', async () => {
    const manifest = createValidManifest({
      requiredPermissions: ['catalog.read', 'catalog.write'],
    });

    const { adapter } = buildFullPipeline([manifest]);

    const request = createSdkRequest({
      identity: createOperatorIdentity('operator-1', ['admin']),
    });

    const response = await adapter.handleRequest(request);
    const body = response.body.data as {
      data: {
        plugins: { id: string }[];
        rejected: unknown[];
      };
    };

    expect(response.status).toBe(200);
    expect(body.data.plugins).toHaveLength(1);
    expect(body.data.plugins[0]?.id).toBe('catalog-admin-ui');
    expect(body.data.rejected).toHaveLength(0);
  });

  it('should filter out plugins requiring permissions missing for viewer role', async () => {
    const manifest = createValidManifest({
      requiredPermissions: ['catalog.read', 'catalog.write'],
    });

    const { adapter } = buildFullPipeline([manifest]);

    const request = createSdkRequest({
      identity: createOperatorIdentity('viewer-operator', ['viewer']),
    });

    const response = await adapter.handleRequest(request);
    const body = response.body.data as {
      data: {
        plugins: unknown[];
        rejected: {
          id: string;
          reasonCode: string;
          message: string;
        }[];
      };
    };

    expect(response.status).toBe(200);
    expect(body.data.plugins).toHaveLength(0);
    expect(body.data.rejected).toHaveLength(1);
    expect(body.data.rejected[0]?.id).toBe('catalog-admin-ui');
    expect(body.data.rejected[0]?.reasonCode).toBe('PERMISSION_FILTERED');
    expect(body.data.rejected[0]?.message).toContain('catalog.write');
  });

  it('should allow operator role with sufficient permissions', async () => {
    const manifest = createValidManifest({
      requiredPermissions: ['catalog.read', 'catalog.write'],
    });

    const { adapter } = buildFullPipeline([manifest]);

    const request = createSdkRequest({
      identity: createOperatorIdentity('operator-1', ['operator']),
    });

    const response = await adapter.handleRequest(request);
    const body = response.body.data as {
      data: {
        plugins: { id: string }[];
        rejected: unknown[];
      };
    };

    expect(response.status).toBe(200);
    expect(body.data.plugins).toHaveLength(1);
    expect(body.data.rejected).toHaveLength(0);
  });

  it('should allow operator with additional inline permissions', async () => {
    const manifest = createValidManifest({
      requiredPermissions: ['catalog.read', 'settings.manage'],
    });

    const { adapter } = buildFullPipeline([manifest]);

    const request = createSdkRequest({
      identity: createOperatorIdentity(
        'operator-1',
        ['operator'],
        ['settings.manage'],
      ),
    });

    const response = await adapter.handleRequest(request);
    const body = response.body.data as {
      data: {
        plugins: { id: string }[];
        rejected: unknown[];
      };
    };

    expect(response.status).toBe(200);
    expect(body.data.plugins).toHaveLength(1);
    expect(body.data.rejected).toHaveLength(0);
  });

  it('should deny action when operator lacks required permissions', async () => {
    const { adapter } = buildFullPipeline([]);

    const request = createSdkRequest({
      method: 'POST',
      path: '/admin/api/v1/action/settings.reset',
      params: { actionId: 'settings.reset' },
      identity: createOperatorIdentity('viewer-operator', ['viewer']),
    });

    const response = await adapter.handleRequest(request);

    expect(response.status).toBe(403);

    const body = response.body.data as {
      error: { code: string; message: string; remediationHint?: string };
    };

    expect(body.error.code).toBe('PERMISSION_REQUIREMENT_NOT_MET');
    expect(body.error.message).toContain('settings.reset');
    expect(body.error.remediationHint).toBeDefined();
  });

  it('should allow action when operator has required permissions', async () => {
    const { adapter } = buildFullPipeline([]);

    const request = createSdkRequest({
      method: 'POST',
      path: '/admin/api/v1/action/catalog.export',
      params: { actionId: 'catalog.export' },
      identity: createOperatorIdentity('operator-1', ['admin']),
    });

    const response = await adapter.handleRequest(request);

    expect(response.status).toBe(200);

    const body = response.body.data as {
      data: { actionId: string; allowed: boolean };
    };

    expect(body.data.actionId).toBe('catalog.export');
    expect(body.data.allowed).toBe(true);
  });

  it('should deny action for unknown actionId', async () => {
    const { adapter } = buildFullPipeline([]);

    const request = createSdkRequest({
      method: 'POST',
      path: '/admin/api/v1/action/nonexistent.action',
      params: { actionId: 'nonexistent.action' },
      identity: createOperatorIdentity('operator-1', ['admin']),
    });

    const response = await adapter.handleRequest(request);

    expect(response.status).toBe(403);

    const body = response.body.data as {
      error: { code: string; message: string };
    };

    expect(body.error.code).toBe('ACTION_GATE_NOT_FOUND');
  });

  it('should mix accepted and permission-filtered plugins for viewer', async () => {
    const unrestrictedManifest = createValidManifest({
      id: 'public-widget',
      version: '1.0.0',
      requiredPermissions: [],
      requiredCapabilities: [],
      extensionPoints: ['widget'],
    });

    const restrictedManifest = createValidManifest({
      id: 'admin-tools',
      version: '1.0.0',
      requiredPermissions: ['settings.manage'],
      requiredCapabilities: [],
      extensionPoints: ['page'],
    });

    const { adapter } = buildFullPipeline([
      unrestrictedManifest,
      restrictedManifest,
    ]);

    const request = createSdkRequest({
      identity: createOperatorIdentity('viewer-operator', ['viewer']),
    });

    const response = await adapter.handleRequest(request);
    const body = response.body.data as {
      data: {
        plugins: { id: string }[];
        rejected: { id: string; reasonCode: string }[];
      };
      diagnostics: {
        acceptedCount: number;
        rejectedCount: number;
      };
    };

    expect(response.status).toBe(200);
    expect(body.data.plugins).toHaveLength(1);
    expect(body.data.plugins[0]?.id).toBe('public-widget');

    expect(body.data.rejected).toHaveLength(1);
    expect(body.data.rejected[0]?.id).toBe('admin-tools');
    expect(body.data.rejected[0]?.reasonCode).toBe('PERMISSION_FILTERED');

    expect(body.diagnostics.acceptedCount).toBe(1);
    expect(body.diagnostics.rejectedCount).toBe(1);
  });

  it('should produce diagnostics with operator-specific metadata', async () => {
    const manifest = createValidManifest({
      requiredPermissions: ['catalog.read'],
    });

    const { adapter } = buildFullPipeline([manifest]);

    const request = createSdkRequest({
      path: '/admin/api/v1/diagnostics',
      identity: createOperatorIdentity('viewer-jane', ['viewer']),
    });

    const response = await adapter.handleRequest(request);
    const body = response.body.data as {
      metadata: {
        subjectId: string;
        roles: string[];
      };
      plugins: {
        subjectId: string;
      }[];
    };

    expect(response.status).toBe(200);
    expect(body.metadata.subjectId).toBe('viewer-jane');
    expect(body.metadata.roles).toEqual(['viewer']);

    for (const plugin of body.plugins) {
      expect(plugin.subjectId).toBe('viewer-jane');
    }
  });
});
