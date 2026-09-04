import { describe, expect, it } from 'vitest';
import {
  ADMIN_UI_PLUGIN_MANIFEST_SCHEMA_VERSION,
  type IAdminUIPluginManifest,
  AdminUIPluginManifestValidationError,
  AdminUIPluginManifestValidator,
} from '@/index.js';

const validManifest: IAdminUIPluginManifest = {
  schemaVersion: ADMIN_UI_PLUGIN_MANIFEST_SCHEMA_VERSION,
  id: 'admin-health',
  version: '1.2.3',
  displayName: 'Health',
  shellCompatibility: '^0.1.0',
  requiredPermissions: ['health.read'],
  requiredCapabilities: ['platform.health'],
  extensionPoints: ['nav', 'page'],
  trustClass: 'internal',
  reviewStatus: 'approved',
  reviewedAt: '2026-06-03T12:00:00.000Z',
  reviewer: 'platform-admin-team',
};

describe('admin UI plugin manifest validation', () => {
  const manifestValidator = new AdminUIPluginManifestValidator();

  it('accepts a valid manifest', () => {
    const parsedManifest = manifestValidator.parse(validManifest);

    expect(parsedManifest.id).toBe(validManifest.id);
    expect(parsedManifest.version).toBe(validManifest.version);
  });

  it('returns failure for schema violations', () => {
    const result = manifestValidator.validate({
      ...validManifest,
      shellCompatibility: 'not-a-range',
    });

    expect(result.success).toBe(false);

    if (result.success) {
      throw new Error('Expected validation failure.');
    }

    expect(result.error).toBeInstanceOf(AdminUIPluginManifestValidationError);
    expect(
      result.error.issues.some((issue) => issue.path === 'shellCompatibility'),
    ).toBe(true);
  });

  it('returns failure for duplicate extension points', () => {
    const result = manifestValidator.validate({
      ...validManifest,
      extensionPoints: ['nav', 'nav'],
    });

    expect(result.success).toBe(false);

    if (result.success) {
      throw new Error('Expected validation failure.');
    }

    expect(
      result.error.issues.some(
        (issue) => issue.code === 'duplicate_extension_point',
      ),
    ).toBe(true);
  });
});
