// Immutable ordered multi-selection helpers (string ids, insertion order, no duplicates).
export type SelectionSet = { readonly ids: readonly string[] };
export type Rect = { x0: number; y0: number; x1: number; y1: number };
export type Box = Rect & { id: string };

const uniq = (ids: readonly string[]): string[] => Array.from(new Set(ids));

export const replace = (ids: readonly string[]): SelectionSet => ({ ids: uniq(ids) });
export const add = (set: SelectionSet, ids: readonly string[]): SelectionSet => ({ ids: uniq([...set.ids, ...ids]) });
export const remove = (set: SelectionSet, ids: readonly string[]): SelectionSet => {
  const drop = new Set(ids);
  return { ids: set.ids.filter(i => !drop.has(i)) };
};
export const toggle = (set: SelectionSet, id: string): SelectionSet =>
  set.ids.includes(id) ? remove(set, [id]) : add(set, [id]);

const norm = (r: Rect) => ({ x0: Math.min(r.x0, r.x1), x1: Math.max(r.x0, r.x1), y0: Math.min(r.y0, r.y1), y1: Math.max(r.y0, r.y1) });

/** Ids of items inside the marquee (fully contained) or touching it (overlap, edges count). */
export function inMarquee(rect: Rect, items: readonly Box[], mode: 'inside' | 'touch'): string[] {
  const m = norm(rect);
  return items.filter(it => {
    const b = norm(it);
    return mode === 'inside'
      ? b.x0 >= m.x0 && b.x1 <= m.x1 && b.y0 >= m.y0 && b.y1 <= m.y1
      : b.x0 <= m.x1 && b.x1 >= m.x0 && b.y0 <= m.y1 && b.y1 >= m.y0;
  }).map(i => i.id);
}

/** Primary = last added id, or null when empty. */
export const primary = (set: SelectionSet): string | null => set.ids.length ? set.ids[set.ids.length - 1] : null;
export const serialize = (set: SelectionSet): string[] => [...set.ids];
