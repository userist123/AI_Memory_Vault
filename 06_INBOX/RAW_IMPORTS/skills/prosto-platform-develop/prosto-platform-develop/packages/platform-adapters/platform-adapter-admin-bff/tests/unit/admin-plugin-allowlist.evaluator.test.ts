import { describe, expect, it } from 'vitest';
import { AdminPluginAllowlistEvaluator } from '@/index.js';

describe('AdminPluginAllowlistEvaluator', () => {
  it('should allow all plugins when allowlist is not required', () => {
    const evaluator = new AdminPluginAllowlistEvaluator({
      entries: [],
      requireAllowlist: false,
    });

    const result = evaluator.evaluate('any-plugin', '1.0.0');

    expect(result.allowed).toBe(true);
  });

  it('should reject plugin not matching any allowlist entry', () => {
    const evaluator = new AdminPluginAllowlistEvaluator({
      entries: [{ pluginIdPattern: 'approved-plugin' }],
      requireAllowlist: true,
    });

    const result = evaluator.evaluate('unknown-plugin', '1.0.0');

    expect(result.allowed).toBe(false);

    if (!result.allowed) {
      expect(result.reasonCode).toBe('ALLOWLIST_REJECTED');
      expect(result.message).toContain('unknown-plugin');
      expect(result.remediationHint).toBeDefined();
    }
  });

  it('should accept plugin matching exact ID pattern', () => {
    const evaluator = new AdminPluginAllowlistEvaluator({
      entries: [{ pluginIdPattern: 'my-plugin' }],
      requireAllowlist: true,
    });

    const result = evaluator.evaluate('my-plugin', '1.0.0');

    expect(result.allowed).toBe(true);
  });

  it('should accept plugin matching wildcard ID pattern', () => {
    const evaluator = new AdminPluginAllowlistEvaluator({
      entries: [{ pluginIdPattern: '*-plugin' }],
      requireAllowlist: true,
    });

    const result = evaluator.evaluate('my-plugin', '1.0.0');

    expect(result.allowed).toBe(true);
  });

  it('should reject plugin when version does not match', () => {
    const evaluator = new AdminPluginAllowlistEvaluator({
      entries: [{ pluginIdPattern: 'my-plugin', versionPattern: '^2.0.0' }],
      requireAllowlist: true,
    });

    const result = evaluator.evaluate('my-plugin', '1.0.0');

    expect(result.allowed).toBe(false);

    if (!result.allowed) {
      expect(result.reasonCode).toBe('ALLOWLIST_REJECTED');
    }
  });

  it('should accept plugin when version matches caret range', () => {
    const evaluator = new AdminPluginAllowlistEvaluator({
      entries: [{ pluginIdPattern: 'my-plugin', versionPattern: '^1.0.0' }],
      requireAllowlist: true,
    });

    const result = evaluator.evaluate('my-plugin', '1.5.0');

    expect(result.allowed).toBe(true);
  });

  it('should accept plugin when version matches tilde range', () => {
    const evaluator = new AdminPluginAllowlistEvaluator({
      entries: [{ pluginIdPattern: 'my-plugin', versionPattern: '~1.2.0' }],
      requireAllowlist: true,
    });

    const result = evaluator.evaluate('my-plugin', '1.2.5');

    expect(result.allowed).toBe(true);
  });

  it('should accept plugin when version matches wildcard pattern', () => {
    const evaluator = new AdminPluginAllowlistEvaluator({
      entries: [{ pluginIdPattern: 'my-plugin', versionPattern: '1.*' }],
      requireAllowlist: true,
    });

    const result = evaluator.evaluate('my-plugin', '1.9.9');

    expect(result.allowed).toBe(true);
  });

  it('should accept plugin when version matches >= pattern', () => {
    const evaluator = new AdminPluginAllowlistEvaluator({
      entries: [{ pluginIdPattern: 'my-plugin', versionPattern: '>=2.0.0' }],
      requireAllowlist: true,
    });

    const result = evaluator.evaluate('my-plugin', '3.0.0');

    expect(result.allowed).toBe(true);
  });

  it('should accept plugin matching any of multiple entries', () => {
    const evaluator = new AdminPluginAllowlistEvaluator({
      entries: [
        { pluginIdPattern: 'plugin-a' },
        { pluginIdPattern: 'plugin-b' },
      ],
      requireAllowlist: true,
    });

    const result = evaluator.evaluate('plugin-b', '1.0.0');

    expect(result.allowed).toBe(true);
  });
});
