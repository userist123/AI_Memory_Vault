// Mișcare nativă (UI/UX P2-13): View Transitions doar unde browserul le are și utilizatorul nu a cerut mișcare redusă;
// altfel schimbarea se face direct, fără animație și fără erori în browserele vechi.
type Doc = { startViewTransition?: (cb: () => void) => unknown };
type Win = { matchMedia?: (q: string) => { matches: boolean } };

export function canViewTransition(d: Doc | undefined, w: Win | undefined): boolean {
  if (!d || typeof d.startViewTransition !== 'function') return false;
  try { return !w?.matchMedia?.('(prefers-reduced-motion: reduce)').matches; } catch { return true; }
}
/** Rulează `update` (care trebuie să schimbe DOM-ul sincron, ex. cu flushSync) într-o tranziție de vedere, dacă se poate. */
export function withViewTransition(update: () => void, d: Doc | undefined = typeof document === 'undefined' ? undefined : document as Doc,
  w: Win | undefined = typeof window === 'undefined' ? undefined : window){
  if (!canViewTransition(d, w)){ update(); return; }
  try { d!.startViewTransition!(update); } catch { update(); }
}
