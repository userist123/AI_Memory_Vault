// Fuzzy matching for the command palette: case- and diacritics-insensitive ("continua" finds "Continuă",
// "santier" finds "Șantier"), subsequence based, with a score that prefers prefixes and word starts.

/** Lower case without diacritics: NFD splits ă/â/î/ș/ț (comma-below and cedilla forms alike) into base + mark. */
export function fold(s: string): string {
  return s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
}

/** Score of `query` against `text`, or null when it does not match. Higher is better. Empty query matches everything with 0. */
export function fuzzyScore(query: string, text: string): number | null {
  const q = fold(query).replace(/\s+/g, ' ').trim(), t = fold(text);
  if (!q) return 0;
  const at = t.indexOf(q);
  if (at >= 0) return 1000 - at * 2 + (at === 0 || /[\s·\-/(]/.test(t[at - 1] ?? '') ? 200 : 0) - (t.length - q.length) * 0.1;
  // every word of the query must appear as a substring (any order) — "plan pdf" finds "Pachet planșe PDF"
  const words = q.split(' ');
  if (words.length > 1 && words.every(w => t.includes(w))) return 600 - t.length * 0.1;
  // subsequence: characters in order, rewarding runs and word starts
  let score = 0, ti = 0, run = 0;
  for (const ch of q.replace(/ /g, '')){
    const k = t.indexOf(ch, ti); if (k < 0) return null;
    run = k === ti ? run + 1 : 0; score += 10 + run * 5 + (k === 0 || /[\s·\-/(]/.test(t[k - 1] ?? '') ? 15 : 0) - Math.min(k - ti, 10);
    ti = k + 1;
  }
  return score;
}

/** Items matching `query`, best first; ties keep the original order. `keys` returns every searchable text of an item. */
export function fuzzyFilter<T>(items: T[], query: string, keys: (x: T) => string[]): T[] {
  return items.map((x, i) => { let best: number | null = null; for (const k of keys(x)){ const s = fuzzyScore(query, k); if (s != null && (best == null || s > best)) best = s; } return { x, i, s: best }; })
    .filter(r => r.s != null).sort((a, b) => (b.s! - a.s!) || (a.i - b.i)).map(r => r.x);
}

/** Arrow / Home / End navigation in the result list (wraps around); returns the new active index, -1 for an empty list. */
export function moveActive(key: string, active: number, count: number): number {
  if (count <= 0) return -1;
  if (key === 'ArrowDown') return active < 0 ? 0 : (active + 1) % count;
  if (key === 'ArrowUp') return active <= 0 ? count - 1 : active - 1;
  if (key === 'Home') return 0;
  if (key === 'End') return count - 1;
  return Math.min(Math.max(active, 0), count - 1);
}
/** Ctrl+K on Windows/Linux, Cmd+K on macOS (either is accepted); never with Alt or Shift. On macOS, Ctrl+K inside a
 *  text field keeps its native meaning (delete to the end of the line): there only Cmd+K opens the palette. */
export function isPaletteShortcut(e: { key: string; ctrlKey: boolean; metaKey: boolean; altKey: boolean; shiftKey: boolean }, ctx: { mac?: boolean; inField?: boolean } = {}): boolean {
  if (e.altKey || e.shiftKey || e.key.toLowerCase() !== 'k') return false;
  if (e.metaKey) return true;
  return e.ctrlKey && !(ctx.mac && ctx.inField);
}
