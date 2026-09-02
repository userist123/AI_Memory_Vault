import type {
  IAdminDiagnosticsRequestContext,
  IAdminDiagnosticsServiceConfig,
} from '@/diagnostics/index.js';
import {
  ADMIN_DIAGNOSTICS_SCHEMA_VERSION,
  AdminDiagnosticsService,
} from '@/diagnostics/index.js';
import type { IAdminDiscoveryResult } from '@/admin-bff.interfaces.js';
import { PlatformDelegatedIdentity } from '@prosto/platform-sdk';
import type { IPlatformDelegatedIdentity } from '@prosto/platform-sdk';
import { describe, expect, it } from 'vitest';

function createMockIdentity(): IPlatformDelegatedIdentity {
  return new PlatformDelegatedIdentity({
    subjectId: 'operator-1',
    roles: ['admin'],
    permissions: ['read', 'write'],
  });
}

function createMockRequestContext(
  overrides?: Partial<IAdminDiagnosticsRequestContext>,
): IAdminDiagnosticsRequestContext {
  return {
    correlationId: 'test-correlation-123',
    identity: createMockIdentity(),
    requestPath: '/admin/api/v1/discovery',
    userAgent: 'Mozilla/5.0',
    clientIp: '127.0.0.1',
    ...overrides,
  };
}

function createMockServiceConfig(
  overrides?: Partial<IAdminDiagnosticsServiceConfig>,
): IAdminDiagnosticsServiceConfig {
  return {
    enableDetailedLogging: true,
    discoveryPipelineVersion: '1.0.0',
    ...overrides,
  };
}

function createMockDiscoveryResult(
  overrides?: Partial<IAdminDiscoveryResult>,
): IAdminDiscoveryResult {
  return {
    payload: {
      schemaVersion: 'admin-discovery-payload.v1',
      generatedAt: new Date().toISOString(),
      plugins: [
        {
          id: 'plugin-a',
          version: '1.0.0',
          displayName: 'Plugin A',
          shellCompatibility: '>=1.0.0',
          trustClass: 'trusted',
          reviewStatus: 'approved',
          extensions: {
            navigation: [],
            pages: [],
            widgets: [],
            actions: [],
          },
        },
      ],
      rejected: [
        {
          id: 'plugin-b',
          version: '2.0.0',
          reasonCode: 'ALLOWLIST_REJECTED',
          message: 'Plugin not in allowlist',
          remediationHint: 'Add to allowlist',
        },
      ],
    },
    diagnostics: {
      acceptedCount: 1,
      rejectedCount: 1,
      duration: 25,
    },
    ...overrides,
  };
}

describe('AdminDiagnosticsService', () => {
  describe('generateDiagnosticsPayload', () => {
    it('should generate diagnostics payload from discovery result', () => {
      const service = new AdminDiagnosticsService(createMockServiceConfig());
      const discoveryResult = createMockDiscoveryResult();
      const requestContext = createMockRequestContext();

      const payload = service.generateDiagnosticsPayload(
        discoveryResult,
        requestContext,
      );

      expect(payload.schemaVersion).toBe(ADMIN_DIAGNOSTICS_SCHEMA_VERSION);
      expect(payload.correlationId).toBe('test-correlation-123');
      expect(payload.plugins).toHaveLength(2);
      expect(payload.summary.acceptedCount).toBe(1);
      expect(payload.summary.rejectedCount).toBe(1);
      expect(payload.summary.totalCount).toBe(2);
      expect(payload.summary.correlationId).toBe('test-correlation-123');
    });

    it('should include environment and shell version when configured', () => {
      const service = new AdminDiagnosticsService(
        createMockServiceConfig({
          environment: 'production',
          shellVersion: '2.0.0',
        }),
      );
      const discoveryResult = createMockDiscoveryResult();
      const requestContext = createMockRequestContext();

      const payload = service.generateDiagnosticsPayload(
        discoveryResult,
        requestContext,
      );

      expect(payload.environment).toBe('production');
      expect(payload.shellVersion).toBe('2.0.0');
      expect(payload.summary.environment).toBe('production');
    });

    it('should map accepted plugins correctly', () => {
      const service = new AdminDiagnosticsService(createMockServiceConfig());
      const discoveryResult = createMockDiscoveryResult();
      const requestContext = createMockRequestContext();

      const payload = service.generateDiagnosticsPayload(
        discoveryResult,
        requestContext,
      );

      const acceptedEntry = payload.plugins.find(
        (p) => p.status === 'accepted',
      );
      expect(acceptedEntry).toBeDefined();
      expect(acceptedEntry?.pluginId).toBe('plugin-a');
      expect(acceptedEntry?.pluginVersion).toBe('1.0.0');
      expect(acceptedEntry?.reasonCode).toBeUndefined();
      expect(acceptedEntry?.remediationHint).toBeUndefined();
    });

    it('should map rejected plugins with reason codes', () => {
      const service = new AdminDiagnosticsService(createMockServiceConfig());
      const discoveryResult = createMockDiscoveryResult();
      const requestContext = createMockRequestContext();

      const payload = service.generateDiagnosticsPayload(
        discoveryResult,
        requestContext,
      );

      const rejectedEntry = payload.plugins.find(
        (p) => p.status === 'rejected',
      );
      expect(rejectedEntry).toBeDefined();
      expect(rejectedEntry?.pluginId).toBe('plugin-b');
      expect(rejectedEntry?.pluginVersion).toBe('2.0.0');
      expect(rejectedEntry?.reasonCode).toBe('ALLOWLIST_REJECTED');
      expect(rejectedEntry?.message).toBe('Plugin not in allowlist');
      expect(rejectedEntry?.remediationHint).toBe('Add to allowlist');
    });

    it('should include metadata with subject and request info', () => {
      const service = new AdminDiagnosticsService(createMockServiceConfig());
      const discoveryResult = createMockDiscoveryResult();
      const requestContext = createMockRequestContext();

      const payload = service.generateDiagnosticsPayload(
        discoveryResult,
        requestContext,
      );

      expect(payload.metadata.subjectId).toBe('operator-1');
      expect(payload.metadata.roles).toEqual(['admin']);
      expect(payload.metadata.requestPath).toBe('/admin/api/v1/discovery');
      expect(payload.metadata.userAgent).toBe('Mozilla/5.0');
      expect(payload.metadata.clientIp).toBe('127.0.0.1');
      expect(payload.metadata.discoveryPipelineVersion).toBe('1.0.0');
    });

    it('should generate timestamp for each entry', () => {
      const service = new AdminDiagnosticsService(createMockServiceConfig());
      const discoveryResult = createMockDiscoveryResult();
      const requestContext = createMockRequestContext();

      const payload = service.generateDiagnosticsPayload(
        discoveryResult,
        requestContext,
      );

      for (const entry of payload.plugins) {
        expect(entry.timestamp).toBeDefined();
        expect(new Date(entry.timestamp).getTime()).not.toBeNaN();
      }
    });

    it('should handle empty discovery result', () => {
      const service = new AdminDiagnosticsService(createMockServiceConfig());
      const discoveryResult: IAdminDiscoveryResult = {
        payload: {
          schemaVersion: 'admin-discovery-payload.v1',
          generatedAt: new Date().toISOString(),
          plugins: [],
          rejected: [],
        },
        diagnostics: {
          acceptedCount: 0,
          rejectedCount: 0,
          duration: 0,
        },
      };
      const requestContext = createMockRequestContext();

      const payload = service.generateDiagnosticsPayload(
        discoveryResult,
        requestContext,
      );

      expect(payload.plugins).toHaveLength(0);
      expect(payload.summary.acceptedCount).toBe(0);
      expect(payload.summary.rejectedCount).toBe(0);
      expect(payload.summary.totalCount).toBe(0);
    });
  });

  describe('createPluginEntry', () => {
    it('should create plugin entry with all required fields', () => {
      const service = new AdminDiagnosticsService(createMockServiceConfig());

      const entry = service.createPluginEntry(
        'test-plugin',
        '1.0.0',
        'accepted',
        undefined,
        undefined,
        undefined,
        'corr-123',
        'operator-1',
      );

      expect(entry.pluginId).toBe('test-plugin');
      expect(entry.pluginVersion).toBe('1.0.0');
      expect(entry.status).toBe('accepted');
      expect(entry.correlationId).toBe('corr-123');
      expect(entry.subjectId).toBe('operator-1');
      expect(entry.timestamp).toBeDefined();
    });

    it('should create rejected entry with reason code and remediation', () => {
      const service = new AdminDiagnosticsService(createMockServiceConfig());

      const entry = service.createPluginEntry(
        'bad-plugin',
        '2.0.0',
        'rejected',
        'TRUST_CLASS_REJECTED',
        'Trust class not allowed',
        'Use trusted class',
        'corr-456',
        'operator-2',
      );

      expect(entry.status).toBe('rejected');
      expect(entry.reasonCode).toBe('TRUST_CLASS_REJECTED');
      expect(entry.message).toBe('Trust class not allowed');
      expect(entry.remediationHint).toBe('Use trusted class');
    });

    it('should include environment and shell version when configured', () => {
      const service = new AdminDiagnosticsService(
        createMockServiceConfig({
          environment: 'staging',
          shellVersion: '1.5.0',
        }),
      );

      const entry = service.createPluginEntry(
        'plugin',
        '1.0.0',
        'accepted',
        undefined,
        undefined,
        undefined,
        'corr-789',
        'operator-3',
      );

      expect(entry.environment).toBe('staging');
      expect(entry.shellVersion).toBe('1.5.0');
    });
  });

  describe('aggregateDiagnostics', () => {
    it('should aggregate multiple discovery results', () => {
      const service = new AdminDiagnosticsService(createMockServiceConfig());
      const result1 = createMockDiscoveryResult({
        diagnostics: { acceptedCount: 2, rejectedCount: 1, duration: 10 },
      });
      const result2 = createMockDiscoveryResult({
        diagnostics: { acceptedCount: 1, rejectedCount: 2, duration: 15 },
      });
      const requestContext = createMockRequestContext();

      const payload = service.aggregateDiagnostics(
        [result1, result2],
        requestContext,
      );

      expect(payload.summary.acceptedCount).toBe(3);
      expect(payload.summary.rejectedCount).toBe(3);
      expect(payload.summary.duration).toBe(25);
      expect(payload.plugins.length).toBeGreaterThan(0);
    });

    it('should merge correlation ID across aggregated results', () => {
      const service = new AdminDiagnosticsService(createMockServiceConfig());
      const result1 = createMockDiscoveryResult();
      const result2 = createMockDiscoveryResult();
      const requestContext = createMockRequestContext({
        correlationId: 'shared-correlation',
      });

      const payload = service.aggregateDiagnostics(
        [result1, result2],
        requestContext,
      );

      expect(payload.correlationId).toBe('shared-correlation');
      expect(payload.summary.correlationId).toBe('shared-correlation');

      for (const entry of payload.plugins) {
        expect(entry.correlationId).toBe('shared-correlation');
      }
    });
  });

  describe('diagnostics schema', () => {
    it('should use correct schema version', () => {
      const service = new AdminDiagnosticsService(createMockServiceConfig());
      const discoveryResult = createMockDiscoveryResult();
      const requestContext = createMockRequestContext();

      const payload = service.generateDiagnosticsPayload(
        discoveryResult,
        requestContext,
      );

      expect(payload.schemaVersion).toBe('admin-diagnostics.v1');
    });
  });
});
