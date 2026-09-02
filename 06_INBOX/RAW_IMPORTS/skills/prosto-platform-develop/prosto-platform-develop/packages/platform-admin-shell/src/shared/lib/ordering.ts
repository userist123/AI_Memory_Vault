/**
 * Stable sort by numeric order field with tiebreaker for equal values.
 * Items with the same order maintain their original relative sequence
 * (tiebreaker preserves insertion order).
 */
export function sortByOrder<T>(
  items: readonly T[],
  orderFn: (item: T) => number,
  tiebreakerFn: (item: T) => number,
): T[] {
  return [...items].sort((a, b) => {
    const orderDiff = orderFn(a) - orderFn(b);

    if (orderDiff !== 0) return orderDiff;

    return tiebreakerFn(a) - tiebreakerFn(b);
  });
}
