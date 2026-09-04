import { describe, expect, it } from 'vitest';
import { assert, AssertionError, dateNowIso } from '@/common/index.js';

describe('assert', () => {
  it('does not throw when condition is truthy', () => {
    expect(() => assert(true, 'should not throw')).not.toThrow();
  });

  it('throws AssertionError when condition is falsy', () => {
    expect(() => assert(false, 'expected failure')).toThrow(AssertionError);
    expect(() => assert(false, 'expected failure')).toThrow('expected failure');
  });
});

describe('dateNowIso', () => {
  it('returns a valid ISO string', () => {
    const result = dateNowIso();
    expect(new Date(result).toISOString()).toBe(result);
  });
});
