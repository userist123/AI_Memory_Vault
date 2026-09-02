import { describe, expect, it } from 'vitest';
import {
  ADMIN_COMPATIBILITY_CONTRACT_VERSION,
  ADMIN_UI_PLUGIN_MANIFEST_SCHEMA_VERSION,
  AdminPluginCompatibilityEvaluator,
  type IAdminUIPluginManifest,
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
};

describe('admin plugin compatibility evaluator', () => {
  const evaluator = new AdminPluginCompatibilityEvaluator();

  it('allows plugin when shell version satisfies manifest range', () => {
    const result = evaluator.evaluate({
      shellVersion: '0.1.5',
      supportedContractVersion: ADMIN_COMPATIBILITY_CONTRACT_VERSION,
      pluginContractVersion: ADMIN_COMPATIBILITY_CONTRACT_VERSION,
      manifest: validManifest,
    });

    expect(result.allowed).toBe(true);
  });

  it('rejects plugin when shell version does not satisfy manifest range', () => {
    const result = evaluator.evaluate({
      shellVersion: '0.2.0',
      supportedContractVersion: ADMIN_COMPATIBILITY_CONTRACT_VERSION,
      pluginContractVersion: ADMIN_COMPATIBILITY_CONTRACT_VERSION,
      manifest: validManifest,
    });

    expect(result.allowed).toBe(false);

    if (result.allowed) {
      throw new Error('Expected compatibility rejection.');
    }

    expect(result.reasonCode).toBe('SHELL_VERSION_MISMATCH');
    expect(result.remediationHint).toContain('compatible admin shell version');
  });

  it('rejects plugin when contract versions differ', () => {
    const result = evaluator.evaluate({
      shellVersion: '0.1.5',
      supportedContractVersion: ADMIN_COMPATIBILITY_CONTRACT_VERSION,
      pluginContractVersion:
        'admin-compatibility.v2' as typeof ADMIN_COMPATIBILITY_CONTRACT_VERSION,
      manifest: validManifest,
    });

    expect(result.allowed).toBe(false);

    if (result.allowed) {
      throw new Error('Expected compatibility rejection.');
    }

    expect(result.reasonCode).toBe('CONTRACT_VERSION_MISMATCH');
  });
});
