import { describe, expect, it } from 'vitest';
import { createEventToken, createServiceToken } from '@/index.js';

describe('tokens', () => {
  it('generate deterministic scoped symbols', () => {
    const healthServiceA = createServiceToken('health.service');
    const healthServiceB = createServiceToken('health.service');
    const authService = createServiceToken('auth.service');
    const healthEvent = createEventToken('health.service');

    expect(healthServiceA).toBe(healthServiceB);
    expect(healthServiceA).not.toBe(authService);
    expect(healthServiceA).not.toBe(healthEvent);
  });
});
