import type { AdminUIPluginReviewStatusType } from '@prosto/platform-admin-contracts';
import { describe, expect, it } from 'vitest';
import { AdminPluginReviewStatusFilter } from '@/index.js';

describe('AdminPluginReviewStatusFilter', () => {
  it('should allow approved status when in allowed list', () => {
    const filter = new AdminPluginReviewStatusFilter({
      allowedReviewStatuses: ['approved'],
    });

    const result = filter.evaluate('approved');

    expect(result.allowed).toBe(true);
  });

  it('should reject pending status when not in allowed list', () => {
    const filter = new AdminPluginReviewStatusFilter({
      allowedReviewStatuses: ['approved'],
    });

    const result = filter.evaluate('pending');

    expect(result.allowed).toBe(false);

    if (!result.allowed) {
      expect(result.reasonCode).toBe('REVIEW_STATUS_REJECTED');
      expect(result.message).toContain('pending');
      expect(result.remediationHint).toContain('approved');
    }
  });

  it('should reject rejected status when not in allowed list', () => {
    const filter = new AdminPluginReviewStatusFilter({
      allowedReviewStatuses: ['approved'],
    });

    const result = filter.evaluate('rejected');

    expect(result.allowed).toBe(false);

    if (!result.allowed) {
      expect(result.reasonCode).toBe('REVIEW_STATUS_REJECTED');
    }
  });

  it('should reject revoked status when not in allowed list', () => {
    const filter = new AdminPluginReviewStatusFilter({
      allowedReviewStatuses: ['approved'],
    });

    const result = filter.evaluate('revoked');

    expect(result.allowed).toBe(false);

    if (!result.allowed) {
      expect(result.reasonCode).toBe('REVIEW_STATUS_REJECTED');
    }
  });

  it('should allow multiple statuses when configured', () => {
    const filter = new AdminPluginReviewStatusFilter({
      allowedReviewStatuses: ['approved', 'pending'],
    });

    expect(filter.evaluate('approved').allowed).toBe(true);
    expect(filter.evaluate('pending').allowed).toBe(true);
    expect(filter.evaluate('rejected').allowed).toBe(false);
  });

  it('should reject unknown review status', () => {
    const filter = new AdminPluginReviewStatusFilter({
      allowedReviewStatuses: ['approved'],
    });

    const result = filter.evaluate(
      'unknown-status' as AdminUIPluginReviewStatusType,
    );

    expect(result.allowed).toBe(false);

    if (!result.allowed) {
      expect(result.reasonCode).toBe('REVIEW_STATUS_REJECTED');
      expect(result.message).toContain('Unknown review status');
    }
  });
});
