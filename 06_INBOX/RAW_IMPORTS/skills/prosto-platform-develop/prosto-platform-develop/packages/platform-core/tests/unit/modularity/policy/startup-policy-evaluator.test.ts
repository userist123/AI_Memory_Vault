import { describe, expect, it } from 'vitest';
import { StartupPolicyEvaluator } from '@/modularity/index.js';

describe('evaluateStartupPolicy', () => {
  const startupPolicyEvaluator = new StartupPolicyEvaluator();

  it('aborts when module is critical regardless of policy mode', () => {
    const result = startupPolicyEvaluator.evaluate({
      policyMode: 'best-effort',
      moduleId: 'mod-a',
      critical: true,
    });

    expect(result.action).toBe('abort');
    expect(result.reason).toContain('critical');
  });

  it('aborts in strict mode for non-critical module', () => {
    const result = startupPolicyEvaluator.evaluate({
      policyMode: 'strict',
      moduleId: 'mod-b',
      critical: false,
    });

    expect(result.action).toBe('abort');
    expect(result.reason).toContain('strict');
  });

  it('skips with degraded in best-effort mode for non-critical module', () => {
    const result = startupPolicyEvaluator.evaluate({
      policyMode: 'best-effort',
      moduleId: 'mod-c',
      critical: false,
    });

    expect(result.action).toBe('continue');
    expect(result.reason).toContain('can continue');
  });
});
