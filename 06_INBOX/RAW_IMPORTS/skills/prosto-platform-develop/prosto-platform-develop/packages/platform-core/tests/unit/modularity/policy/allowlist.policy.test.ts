import { describe, expect, it } from 'vitest';
import { AllowlistPolicyEvaluator, ModulePolicyReasonCode } from '@/index.js';
import type { IPlatformModuleManifest } from '@prosto/platform-sdk';

// Helper to create a test manifest
function createTestManifest(
  overrides: Partial<IPlatformModuleManifest> = {},
): IPlatformModuleManifest {
  return {
    id: 'test-module',
    version: '1.0.0',
    sdkVersion: '^0.1.0',
    title: 'Test Module',
    dependencies: [],
    ...overrides,
  };
}

describe('AllowlistPolicyEvaluator', () => {
  it('should allow module when it matches allowlist entry', () => {
    const policy = new AllowlistPolicyEvaluator({
      environment: 'production',
      allowlist: [{ moduleIdPattern: 'test-*' }],
      requireAllowlist: true,
    });

    const manifest = createTestManifest();
    const result = policy.evaluate(manifest);

    expect(result.allowed).toBe(true);
    expect(result.reasonCode).toBe(ModulePolicyReasonCode.Allowed);
  });

  it('should reject module not in allowlist', () => {
    const policy = new AllowlistPolicyEvaluator({
      environment: 'production',
      allowlist: [{ moduleIdPattern: 'other-*' }],
      requireAllowlist: true,
    });

    const manifest = createTestManifest();
    const result = policy.evaluate(manifest);

    expect(result.allowed).toBe(false);
    expect(result.reasonCode).toBe(ModulePolicyReasonCode.NotInAllowlist);
  });

  it('should match version patterns with caret range', () => {
    const policy = new AllowlistPolicyEvaluator({
      environment: 'production',
      allowlist: [{ moduleIdPattern: 'test-*', versionPattern: '^1.0.0' }],
      requireAllowlist: true,
    });

    const manifest = createTestManifest({ version: '1.2.0' });
    const result = policy.evaluate(manifest);

    expect(result.allowed).toBe(true);
  });

  it('should reject version not matching pattern', () => {
    const policy = new AllowlistPolicyEvaluator({
      environment: 'production',
      allowlist: [{ moduleIdPattern: 'test-*', versionPattern: '^2.0.0' }],
      requireAllowlist: true,
    });

    const manifest = createTestManifest({ version: '1.0.0' });
    const result = policy.evaluate(manifest);

    expect(result.allowed).toBe(false);
  });

  it('should support wildcard patterns in moduleId', () => {
    const policy = new AllowlistPolicyEvaluator({
      environment: 'production',
      allowlist: [{ moduleIdPattern: '@prosto/*' }],
      requireAllowlist: true,
    });

    const manifest = createTestManifest({ id: '@prosto/auth' });
    const result = policy.evaluate(manifest);

    expect(result.allowed).toBe(true);
  });
});
