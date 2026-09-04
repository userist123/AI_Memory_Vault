import type {
  AdminPluginCompatibilityResultType,
  AdminUIPluginManifestValidationResultType,
  IAdminPluginCompatibilityEvaluator,
  IAdminUIPluginManifest,
  IAdminUIPluginManifestValidator,
} from '@prosto/platform-admin-contracts';
import type { IPlatformDelegatedIdentity } from '@prosto/platform-sdk';
import type { IAdminPluginCatalogSource } from '@/index.js';
import { ADMIN_UI_PLUGIN_MANIFEST_SCHEMA_VERSION } from '@prosto/platform-admin-contracts';
import { describe, expect, it, vi } from 'vitest';
import { AdminDiscoveryAggregationService } from '@/index.js';

function createMockManifest(
  overrides?: Partial<IAdminUIPluginManifest>,
): IAdminUIPluginManifest {
  return {
    schemaVersion: ADMIN_UI_PLUGIN_MANIFEST_SCHEMA_VERSION,
    id: 'test-plugin',
    version: '1.0.0',
    shellCompatibility: '>=1.0.0',
    trustClass: 'internal',
    reviewStatus: 'approved',
    requiredPermissions: [],
    requiredCapabilities: [],
    extensionPoints: ['nav', 'page'],
    displayName: 'Test Plugin',
    ...overrides,
  };
}

function createMockCatalogSource(
  manifests: unknown[] = [],
): IAdminPluginCatalogSource {
  return {
    fetchUIPluginManifests: vi.fn().mockResolvedValue(manifests),
  };
}

function createMockManifestValidator(
  result?: AdminUIPluginManifestValidationResultType,
): IAdminUIPluginManifestValidator {
  const defaultResult: AdminUIPluginManifestValidationResultType = {
    success: true,
    manifest: createMockManifest(),
  };

  return {
    validate: vi.fn().mockReturnValue(result ?? defaultResult),
    parse: vi.fn(),
  };
}

function createMockCompatibilityEvaluator(
  result?: AdminPluginCompatibilityResultType,
): IAdminPluginCompatibilityEvaluator {
  const defaultResult: AdminPluginCompatibilityResultType = {
    allowed: true,
    contractVersion: 'admin-compatibility.v1',
  };

  return {
    evaluate: vi.fn().mockReturnValue(result ?? defaultResult),
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

describe('AdminDiscoveryAggregationService', () => {
  it('should discover plugins from catalog source', async () => {
    const manifest = createMockManifest();
    const catalog = createMockCatalogSource([manifest]);
    const validator = createMockManifestValidator();
    const compatibility = createMockCompatibilityEvaluator();

    const service = new AdminDiscoveryAggregationService(
      catalog,
      validator,
      compatibility,
      {
        shellVersion: '1.0.0',
        supportedContractVersion: 'admin-compatibility.v1',
      },
    );

    const result = await service.discover(createMockDelegatedIdentity());

    expect(result.payload.plugins).toHaveLength(1);
    expect(result.payload.rejected).toHaveLength(0);
    expect(result.diagnostics.acceptedCount).toBe(1);
    expect(result.diagnostics.rejectedCount).toBe(0);
    expect(result.payload.schemaVersion).toBe('admin-discovery-payload.v1');
  });

  it('should return empty results when catalog is empty', async () => {
    const catalog = createMockCatalogSource([]);
    const validator = createMockManifestValidator();
    const compatibility = createMockCompatibilityEvaluator();

    const service = new AdminDiscoveryAggregationService(
      catalog,
      validator,
      compatibility,
      {
        shellVersion: '1.0.0',
        supportedContractVersion: 'admin-compatibility.v1',
      },
    );

    const result = await service.discover(createMockDelegatedIdentity());

    expect(result.payload.plugins).toHaveLength(0);
    expect(result.payload.rejected).toHaveLength(0);
    expect(result.diagnostics.acceptedCount).toBe(0);
    expect(result.diagnostics.rejectedCount).toBe(0);
  });

  it('should reject manifests that fail validation', async () => {
    const catalog = createMockCatalogSource([{ invalid: 'manifest' }]);
    const validator = createMockManifestValidator({
      success: false,
      error: Object.assign(new Error('Validation failed'), {
        issues: [{ code: 'invalid_type', message: 'Required', path: 'id' }],
      }),
    });
    const compatibility = createMockCompatibilityEvaluator();

    const service = new AdminDiscoveryAggregationService(
      catalog,
      validator,
      compatibility,
      {
        shellVersion: '1.0.0',
        supportedContractVersion: 'admin-compatibility.v1',
      },
    );

    const result = await service.discover(createMockDelegatedIdentity());

    expect(result.payload.plugins).toHaveLength(0);
    expect(result.payload.rejected).toHaveLength(1);
    expect(result.diagnostics.acceptedCount).toBe(0);
    expect(result.diagnostics.rejectedCount).toBe(1);
    expect(result.payload.rejected[0]).toMatchObject({
      reasonCode: 'MANIFEST_VALIDATION_FAILED',
      message: 'Validation failed',
      remediationHint: 'Fix manifest validation errors and republish.',
    });
  });

  it('should reject plugins that fail compatibility check', async () => {
    const manifest = createMockManifest();
    const catalog = createMockCatalogSource([manifest]);
    const validator = createMockManifestValidator();
    const compatibility = createMockCompatibilityEvaluator({
      allowed: false,
      contractVersion: 'admin-compatibility.v1',
      reasonCode: 'SHELL_VERSION_MISMATCH',
      message: 'Shell version mismatch',
      remediationHint: 'Upgrade shell',
    });

    const service = new AdminDiscoveryAggregationService(
      catalog,
      validator,
      compatibility,
      {
        shellVersion: '1.0.0',
        supportedContractVersion: 'admin-compatibility.v1',
      },
    );

    const result = await service.discover(createMockDelegatedIdentity());

    expect(result.payload.plugins).toHaveLength(0);
    expect(result.payload.rejected).toHaveLength(1);
    expect(result.diagnostics.acceptedCount).toBe(0);
    expect(result.diagnostics.rejectedCount).toBe(1);
    expect(result.payload.rejected[0]).toMatchObject({
      id: 'test-plugin',
      version: '1.0.0',
      reasonCode: 'SHELL_VERSION_MISMATCH',
      message: 'Shell version mismatch',
      remediationHint: 'Upgrade shell',
    });
  });

  it('should map accepted manifests to plugin descriptors', async () => {
    const manifest = createMockManifest({
      id: 'catalog-admin-ui',
      version: '2.0.0',
      displayName: 'Catalog Admin UI',
      trustClass: 'trusted',
      reviewStatus: 'approved',
      shellCompatibility: '>=1.5.0',
      metadata: { author: 'test' },
    });
    const catalog = createMockCatalogSource([manifest]);
    const validator = createMockManifestValidator({
      success: true,
      manifest,
    });
    const compatibility = createMockCompatibilityEvaluator();

    const service = new AdminDiscoveryAggregationService(
      catalog,
      validator,
      compatibility,
      {
        shellVersion: '1.5.0',
        supportedContractVersion: 'admin-compatibility.v1',
      },
    );

    const result = await service.discover(createMockDelegatedIdentity());

    expect(result.payload.plugins).toHaveLength(1);

    const plugin = result.payload.plugins[0];

    expect(plugin?.id).toBe('catalog-admin-ui');
    expect(plugin?.version).toBe('2.0.0');
    expect(plugin?.displayName).toBe('Catalog Admin UI');
    expect(plugin?.trustClass).toBe('trusted');
    expect(plugin?.reviewStatus).toBe('approved');
    expect(plugin?.shellCompatibility).toBe('>=1.5.0');
    expect(plugin?.extensions).toEqual({
      navigation: [],
      pages: [],
      widgets: [],
      actions: [],
    });
    expect(plugin?.metadata).toEqual({ author: 'test' });
  });

  it('should handle mixed valid and invalid manifests', async () => {
    const validManifest = createMockManifest({ id: 'valid-plugin' });
    const invalidManifest = { id: 'invalid-plugin', broken: true };

    const catalog = createMockCatalogSource([validManifest, invalidManifest]);

    let callCount = 0;
    const validatorSpy = {
      validate: vi.fn().mockImplementation(() => {
        callCount++;

        if (callCount === 1) {
          return {
            success: true,
            manifest: createMockManifest({ id: 'valid-plugin' }),
          };
        }

        return {
          success: false,
          error: Object.assign(new Error('Invalid manifest'), {
            issues: [
              {
                code: 'invalid_type',
                message: 'Required',
                path: 'schemaVersion',
              },
            ],
          }),
        };
      }),

      parse: vi.fn(),
    };

    const compatibility = createMockCompatibilityEvaluator();

    const service = new AdminDiscoveryAggregationService(
      catalog,
      validatorSpy,
      compatibility,
      {
        shellVersion: '1.0.0',
        supportedContractVersion: 'admin-compatibility.v1',
      },
    );

    const result = await service.discover(createMockDelegatedIdentity());

    expect(result.payload.plugins).toHaveLength(1);
    expect(result.payload.rejected).toHaveLength(1);
    expect(result.diagnostics.acceptedCount).toBe(1);
    expect(result.diagnostics.rejectedCount).toBe(1);
  });

  it('should call catalog source to fetch manifests', async () => {
    const catalog = createMockCatalogSource([]);
    const validator = createMockManifestValidator();
    const compatibility = createMockCompatibilityEvaluator();

    const service = new AdminDiscoveryAggregationService(
      catalog,
      validator,
      compatibility,
      {
        shellVersion: '1.0.0',
        supportedContractVersion: 'admin-compatibility.v1',
      },
    );

    await service.discover(createMockDelegatedIdentity());

    expect(catalog.fetchUIPluginManifests).toHaveBeenCalledTimes(1);
  });

  it('should call manifest validator for each manifest', async () => {
    const manifests = [
      createMockManifest({ id: 'plugin-1' }),
      createMockManifest({ id: 'plugin-2' }),
      createMockManifest({ id: 'plugin-3' }),
    ];
    const catalog = createMockCatalogSource(manifests);
    const validator = createMockManifestValidator();
    const compatibility = createMockCompatibilityEvaluator();

    const service = new AdminDiscoveryAggregationService(
      catalog,
      validator,
      compatibility,
      {
        shellVersion: '1.0.0',
        supportedContractVersion: 'admin-compatibility.v1',
      },
    );

    await service.discover(createMockDelegatedIdentity());

    expect(validator.validate).toHaveBeenCalledTimes(3);
  });

  it('should call compatibility evaluator for validated manifests', async () => {
    const manifests = [
      createMockManifest({ id: 'plugin-1' }),
      createMockManifest({ id: 'plugin-2' }),
    ];
    const catalog = createMockCatalogSource(manifests);
    const validator = createMockManifestValidator();
    const compatibility = createMockCompatibilityEvaluator();

    const service = new AdminDiscoveryAggregationService(
      catalog,
      validator,
      compatibility,
      {
        shellVersion: '1.0.0',
        supportedContractVersion: 'admin-compatibility.v1',
      },
    );

    await service.discover(createMockDelegatedIdentity());

    expect(compatibility.evaluate).toHaveBeenCalledTimes(2);
  });

  it('should include validation issues in rejection details', async () => {
    const catalog = createMockCatalogSource([{ invalid: true }]);
    const validator = createMockManifestValidator({
      success: false,
      error: Object.assign(new Error('Validation failed'), {
        issues: [
          { code: 'invalid_type', message: 'Required field', path: 'id' },
          { code: 'invalid_type', message: 'Required field', path: 'version' },
        ],
      }),
    });
    const compatibility = createMockCompatibilityEvaluator();

    const service = new AdminDiscoveryAggregationService(
      catalog,
      validator,
      compatibility,
      {
        shellVersion: '1.0.0',
        supportedContractVersion: 'admin-compatibility.v1',
      },
    );

    const result = await service.discover(createMockDelegatedIdentity());

    expect(result.payload.rejected[0]).toMatchObject({
      details: {
        id: 'Required field',
        version: 'Required field',
      },
    });
  });

  it('should record duration in diagnostics', async () => {
    const catalog = createMockCatalogSource([]);
    const validator = createMockManifestValidator();
    const compatibility = createMockCompatibilityEvaluator();

    const service = new AdminDiscoveryAggregationService(
      catalog,
      validator,
      compatibility,
      {
        shellVersion: '1.0.0',
        supportedContractVersion: 'admin-compatibility.v1',
      },
    );

    const result = await service.discover(createMockDelegatedIdentity());

    expect(result.diagnostics.duration).toBeGreaterThanOrEqual(0);
    expect(typeof result.diagnostics.duration).toBe('number');
  });

  describe('policy checks', () => {
    it('should reject plugin not in allowlist', async () => {
      const manifest = createMockManifest({ id: 'unlisted-plugin' });
      const catalog = createMockCatalogSource([manifest]);
      const validator = createMockManifestValidator({
        success: true,
        manifest,
      });
      const compatibility = createMockCompatibilityEvaluator();
      const allowlistEvaluator = {
        evaluate: vi.fn().mockReturnValue({
          allowed: false,
          reasonCode: 'ALLOWLIST_REJECTED',
          message: 'Plugin "unlisted-plugin@1.0.0" is not in the allowlist.',
          remediationHint: 'Add the plugin to the allowlist.',
        }),
      };

      const service = new AdminDiscoveryAggregationService(
        catalog,
        validator,
        compatibility,
        {
          shellVersion: '1.0.0',
          supportedContractVersion: 'admin-compatibility.v1',
        },
        { allowlistEvaluator },
      );

      const result = await service.discover(createMockDelegatedIdentity());

      expect(result.payload.plugins).toHaveLength(0);
      expect(result.payload.rejected).toHaveLength(1);
      expect(result.payload.rejected[0]).toMatchObject({
        id: 'unlisted-plugin',
        version: '1.0.0',
        reasonCode: 'ALLOWLIST_REJECTED',
        message: 'Plugin "unlisted-plugin@1.0.0" is not in the allowlist.',
      });
      expect(allowlistEvaluator.evaluate).toHaveBeenCalledWith(
        'unlisted-plugin',
        '1.0.0',
      );
    });

    it('should accept plugin that passes allowlist check', async () => {
      const manifest = createMockManifest({ id: 'allowed-plugin' });
      const catalog = createMockCatalogSource([manifest]);
      const validator = createMockManifestValidator({
        success: true,
        manifest,
      });
      const compatibility = createMockCompatibilityEvaluator();
      const allowlistEvaluator = {
        evaluate: vi.fn().mockReturnValue({ allowed: true }),
      };

      const service = new AdminDiscoveryAggregationService(
        catalog,
        validator,
        compatibility,
        {
          shellVersion: '1.0.0',
          supportedContractVersion: 'admin-compatibility.v1',
        },
        { allowlistEvaluator },
      );

      const result = await service.discover(createMockDelegatedIdentity());

      expect(result.payload.plugins).toHaveLength(1);
      expect(result.payload.rejected).toHaveLength(0);
    });

    it('should reject plugin with disallowed trust class', async () => {
      const manifest = createMockManifest({
        trustClass: 'third-party-reviewed',
      });
      const catalog = createMockCatalogSource([manifest]);
      const validator = createMockManifestValidator({
        success: true,
        manifest,
      });
      const compatibility = createMockCompatibilityEvaluator();
      const trustClassFilter = {
        evaluate: vi.fn().mockReturnValue({
          allowed: false,
          reasonCode: 'TRUST_CLASS_REJECTED',
          message:
            'Trust class "third-party-reviewed" is not allowed in production environment.',
          remediationHint:
            'Use a trust class from the allowed set: trusted, internal.',
        }),
      };

      const service = new AdminDiscoveryAggregationService(
        catalog,
        validator,
        compatibility,
        {
          shellVersion: '1.0.0',
          supportedContractVersion: 'admin-compatibility.v1',
        },
        { trustClassFilter },
      );

      const result = await service.discover(createMockDelegatedIdentity());

      expect(result.payload.plugins).toHaveLength(0);
      expect(result.payload.rejected).toHaveLength(1);
      expect(result.payload.rejected[0]).toMatchObject({
        id: 'test-plugin',
        reasonCode: 'TRUST_CLASS_REJECTED',
      });
      expect(trustClassFilter.evaluate).toHaveBeenCalledWith(
        'third-party-reviewed',
      );
    });

    it('should reject plugin with non-approved review status', async () => {
      const manifest = createMockManifest({ reviewStatus: 'pending' });
      const catalog = createMockCatalogSource([manifest]);
      const validator = createMockManifestValidator({
        success: true,
        manifest,
      });
      const compatibility = createMockCompatibilityEvaluator();
      const reviewStatusFilter = {
        evaluate: vi.fn().mockReturnValue({
          allowed: false,
          reasonCode: 'REVIEW_STATUS_REJECTED',
          message: 'Review status "pending" does not permit plugin admission.',
          remediationHint: 'Plugin must have review status: approved.',
        }),
      };

      const service = new AdminDiscoveryAggregationService(
        catalog,
        validator,
        compatibility,
        {
          shellVersion: '1.0.0',
          supportedContractVersion: 'admin-compatibility.v1',
        },
        { reviewStatusFilter },
      );

      const result = await service.discover(createMockDelegatedIdentity());

      expect(result.payload.plugins).toHaveLength(0);
      expect(result.payload.rejected).toHaveLength(1);
      expect(result.payload.rejected[0]).toMatchObject({
        id: 'test-plugin',
        reasonCode: 'REVIEW_STATUS_REJECTED',
      });
      expect(reviewStatusFilter.evaluate).toHaveBeenCalledWith('pending');
    });

    it('should accept plugin passing all three policy checks', async () => {
      const manifest = createMockManifest();
      const catalog = createMockCatalogSource([manifest]);
      const validator = createMockManifestValidator({
        success: true,
        manifest,
      });
      const compatibility = createMockCompatibilityEvaluator();
      const allowlistEvaluator = {
        evaluate: vi.fn().mockReturnValue({ allowed: true }),
      };
      const trustClassFilter = {
        evaluate: vi.fn().mockReturnValue({ allowed: true }),
      };
      const reviewStatusFilter = {
        evaluate: vi.fn().mockReturnValue({ allowed: true }),
      };

      const service = new AdminDiscoveryAggregationService(
        catalog,
        validator,
        compatibility,
        {
          shellVersion: '1.0.0',
          supportedContractVersion: 'admin-compatibility.v1',
        },
        { allowlistEvaluator, trustClassFilter, reviewStatusFilter },
      );

      const result = await service.discover(createMockDelegatedIdentity());

      expect(result.payload.plugins).toHaveLength(1);
      expect(result.payload.rejected).toHaveLength(0);
      expect(allowlistEvaluator.evaluate).toHaveBeenCalledOnce();
      expect(trustClassFilter.evaluate).toHaveBeenCalledOnce();
      expect(reviewStatusFilter.evaluate).toHaveBeenCalledOnce();
    });

    it('should short-circuit policy checks on first rejection', async () => {
      const manifest = createMockManifest();
      const catalog = createMockCatalogSource([manifest]);
      const validator = createMockManifestValidator({
        success: true,
        manifest,
      });
      const compatibility = createMockCompatibilityEvaluator();
      const allowlistEvaluator = {
        evaluate: vi.fn().mockReturnValue({
          allowed: false,
          reasonCode: 'ALLOWLIST_REJECTED',
          message: 'Not in allowlist',
          remediationHint: 'Add to allowlist',
        }),
      };
      const trustClassFilter = {
        evaluate: vi.fn().mockReturnValue({ allowed: true }),
      };
      const reviewStatusFilter = {
        evaluate: vi.fn().mockReturnValue({ allowed: true }),
      };

      const service = new AdminDiscoveryAggregationService(
        catalog,
        validator,
        compatibility,
        {
          shellVersion: '1.0.0',
          supportedContractVersion: 'admin-compatibility.v1',
        },
        { allowlistEvaluator, trustClassFilter, reviewStatusFilter },
      );

      const result = await service.discover(createMockDelegatedIdentity());

      expect(result.payload.plugins).toHaveLength(0);
      expect(result.payload.rejected).toHaveLength(1);
      expect(trustClassFilter.evaluate).not.toHaveBeenCalled();
      expect(reviewStatusFilter.evaluate).not.toHaveBeenCalled();
    });

    it('should work without policy evaluators (backward compatible)', async () => {
      const manifest = createMockManifest();
      const catalog = createMockCatalogSource([manifest]);
      const validator = createMockManifestValidator({
        success: true,
        manifest,
      });
      const compatibility = createMockCompatibilityEvaluator();

      const service = new AdminDiscoveryAggregationService(
        catalog,
        validator,
        compatibility,
        {
          shellVersion: '1.0.0',
          supportedContractVersion: 'admin-compatibility.v1',
        },
      );

      const result = await service.discover(createMockDelegatedIdentity());

      expect(result.payload.plugins).toHaveLength(1);
      expect(result.payload.rejected).toHaveLength(0);
    });

    it('should process mixed manifests with policy rejections and acceptances', async () => {
      const allowedManifest = createMockManifest({ id: 'allowed-plugin' });
      const unlistedManifest = createMockManifest({ id: 'unlisted-plugin' });
      const pendingManifest = createMockManifest({
        id: 'pending-plugin',
        reviewStatus: 'pending',
      });

      const catalog = createMockCatalogSource([
        allowedManifest,
        unlistedManifest,
        pendingManifest,
      ]);

      let callCount = 0;
      const validatorSpy = {
        validate: vi.fn().mockImplementation(() => {
          callCount++;
          const manifests = [
            allowedManifest,
            unlistedManifest,
            pendingManifest,
          ];
          return { success: true, manifest: manifests[callCount - 1] };
        }),
        parse: vi.fn(),
      };

      const compatibility = createMockCompatibilityEvaluator();
      const allowlistEvaluator = {
        evaluate: vi.fn().mockImplementation((id: string) => {
          if (id === 'unlisted-plugin') {
            return {
              allowed: false,
              reasonCode: 'ALLOWLIST_REJECTED',
              message: `Plugin "${id}" is not in the allowlist.`,
              remediationHint: 'Add to allowlist',
            };
          }
          return { allowed: true };
        }),
      };
      const trustClassFilter = {
        evaluate: vi.fn().mockReturnValue({ allowed: true }),
      };
      const reviewStatusFilter = {
        evaluate: vi.fn().mockImplementation((status: string) => {
          if (status === 'pending') {
            return {
              allowed: false,
              reasonCode: 'REVIEW_STATUS_REJECTED',
              message: `Review status "${status}" does not permit admission.`,
              remediationHint: 'Plugin must be approved',
            };
          }
          return { allowed: true };
        }),
      };

      const service = new AdminDiscoveryAggregationService(
        catalog,
        validatorSpy,
        compatibility,
        {
          shellVersion: '1.0.0',
          supportedContractVersion: 'admin-compatibility.v1',
        },
        { allowlistEvaluator, trustClassFilter, reviewStatusFilter },
      );

      const result = await service.discover(createMockDelegatedIdentity());

      expect(result.payload.plugins).toHaveLength(1);
      expect(result.payload.rejected).toHaveLength(2);
      expect(result.payload.plugins[0]?.id).toBe('allowed-plugin');
      expect(result.diagnostics.acceptedCount).toBe(1);
      expect(result.diagnostics.rejectedCount).toBe(2);

      const rejectedIds = result.payload.rejected.map((r) => r.id);
      expect(rejectedIds).toContain('unlisted-plugin');
      expect(rejectedIds).toContain('pending-plugin');
    });
  });
});
