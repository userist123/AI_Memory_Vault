import { describe, expect, it } from 'vitest';
import { sortByOrder } from '@/shared/lib/ordering.js';

describe('sortByOrder', () => {
  it('should sort items by order field', () => {
    const items = [
      { name: 'c', order: 30 },
      { name: 'a', order: 10 },
      { name: 'b', order: 20 },
    ];

    const sorted = sortByOrder(
      items,
      (i) => i.order,
      (i) => items.findIndex((item) => item.name === i.name),
    );

    expect(sorted.map((i) => i.name)).toEqual(['a', 'b', 'c']);
  });

  it('should use tiebreaker for equal order values', () => {
    const items = [
      { name: 'second', order: 10 },
      { name: 'first', order: 10 },
      { name: 'third', order: 10 },
    ];

    const sorted = sortByOrder(
      items,
      (i) => i.order,
      (i) => {
        const order = ['first', 'second', 'third'];

        return order.indexOf(i.name);
      },
    );

    expect(sorted.map((i) => i.name)).toEqual(['first', 'second', 'third']);
  });

  it('should not mutate original array', () => {
    const items = [
      { name: 'b', order: 20 },
      { name: 'a', order: 10 },
    ];
    const original = [...items];

    sortByOrder(
      items,
      (i) => i.order,
      () => 0,
    );

    expect(items).toEqual(original);
  });

  it('should handle empty array', () => {
    const sorted = sortByOrder(
      [],
      () => 0,
      () => 0,
    );

    expect(sorted).toEqual([]);
  });

  it('should handle single item', () => {
    const items = [{ name: 'only', order: 5 }];

    const sorted = sortByOrder(
      items,
      (i) => i.order,
      () => 0,
    );

    expect(sorted).toHaveLength(1);
    expect(sorted[0]?.name).toBe('only');
  });

  it('should handle negative order values', () => {
    const items = [
      { name: 'zero', order: 0 },
      { name: 'negative', order: -10 },
      { name: 'positive', order: 10 },
    ];

    const sorted = sortByOrder(
      items,
      (i) => i.order,
      (i) => items.findIndex((item) => item.name === i.name),
    );

    expect(sorted.map((i) => i.name)).toEqual(['negative', 'zero', 'positive']);
  });

  it('should be stable for equal order and tiebreaker', () => {
    const items = [
      { name: 'a', order: 10, id: 1 },
      { name: 'a', order: 10, id: 2 },
      { name: 'a', order: 10, id: 3 },
    ];

    const sorted = sortByOrder(
      items,
      (i) => i.order,
      () => 0,
    );

    expect(sorted.map((i) => i.id)).toEqual([1, 2, 3]);
  });
});
