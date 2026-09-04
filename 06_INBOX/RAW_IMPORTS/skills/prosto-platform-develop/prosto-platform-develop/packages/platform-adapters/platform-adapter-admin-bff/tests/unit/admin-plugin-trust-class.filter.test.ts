import type { AdminUIPluginTrustClassType } from '@prosto/platform-admin-contracts';
import { describe, expect, it } from 'vitest';
import { AdminPluginTrustClassFilter } from '@/index.js';

describe('AdminPluginTrustClassFilter', () => {
  it('should allow trusted class when in allowed list', () => {
    const filter = new AdminPluginTrustClassFilter({
      allowedTrustClasses: ['trusted', 'internal'],
      environment: 'production',
    });

    const result = filter.evaluate('trusted');

    expect(result.allowed).toBe(true);
  });

  it('should allow internal class when in allowed list', () => {
    const filter = new AdminPluginTrustClassFilter({
      allowedTrustClasses: ['trusted', 'internal'],
      environment: 'production',
    });

    const result = filter.evaluate('internal');

    expect(result.allowed).toBe(true);
  });

  it('should reject third-party-reviewed when not in allowed list', () => {
    const filter = new AdminPluginTrustClassFilter({
      allowedTrustClasses: ['trusted', 'internal'],
      environment: 'production',
    });

    const result = filter.evaluate('third-party-reviewed');

    expect(result.allowed).toBe(false);

    if (!result.allowed) {
      expect(result.reasonCode).toBe('TRUST_CLASS_REJECTED');
      expect(result.message).toContain('third-party-reviewed');
      expect(result.message).toContain('production');
      expect(result.remediationHint).toContain('trusted');
      expect(result.remediationHint).toContain('internal');
    }
  });

  it('should allow all trust classes when all are in allowed list', () => {
    const filter = new AdminPluginTrustClassFilter({
      allowedTrustClasses: ['trusted', 'internal', 'third-party-reviewed'],
    });

    expect(filter.evaluate('trusted').allowed).toBe(true);
    expect(filter.evaluate('internal').allowed).toBe(true);
    expect(filter.evaluate('third-party-reviewed').allowed).toBe(true);
  });

  it('should reject unknown trust class', () => {
    const filter = new AdminPluginTrustClassFilter({
      allowedTrustClasses: ['trusted', 'internal'],
    });

    const result = filter.evaluate(
      'unknown-class' as AdminUIPluginTrustClassType,
    );

    expect(result.allowed).toBe(false);

    if (!result.allowed) {
      expect(result.reasonCode).toBe('TRUST_CLASS_REJECTED');
      expect(result.message).toContain('Unknown trust class');
    }
  });

  it('should include environment label in message', () => {
    const filter = new AdminPluginTrustClassFilter({
      allowedTrustClasses: ['trusted'],
      environment: 'staging',
    });

    const result = filter.evaluate('internal');

    expect(result.allowed).toBe(false);

    if (!result.allowed) {
      expect(result.message).toContain('staging');
    }
  });
});
