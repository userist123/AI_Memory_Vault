// Salvarea ciornei din editor, fără React (testabilă în node). Orice salvare — automată, înainte de o revizie, înainte de
// un link pentru client sau de aplicarea unui design — trece prin putDraft și trimite versiunea de la care a pornit
// (`base`, adică draft_rev): dacă proiectul s-a schimbat între timp pe alt dispozitiv, serverul răspunde 409 și nimic nu
// se suprascrie tăcut. Rezultatul nu aruncă niciodată: rețeaua căzută devine `offline`, sesiunea expirată `auth`.
export type SaveOutcome =
  | { kind: 'saved'; draftRev: number | null }
  | { kind: 'conflict' }
  | { kind: 'auth' }
  | { kind: 'offline' }
  | { kind: 'error'; message: string | null; retry: boolean };

type FetchLike = (url: string, init?: RequestInit) => Promise<Response>;

/** Corpul cererii de salvare: `base` lipsește doar la „păstrează ce am aici” (force) sau când versiunea nu e cunoscută. */
export function draftBody(snapshot: unknown, base: number | null, force = false){
  return JSON.stringify({ snapshot, ...(force || base == null ? {} : { base }) });
}

export async function putDraft(fetchImpl: FetchLike, id: string, snapshot: unknown, base: number | null, force = false): Promise<SaveOutcome> {
  let r: Response;
  try { r = await fetchImpl(`/api/projects/${id}`, { method: 'PUT', headers: { 'content-type': 'application/json' }, body: draftBody(snapshot, base, force) }); }
  catch { return { kind: 'offline' }; }
  const j: any = await r.json().catch(() => ({}));
  if (r.ok) return { kind: 'saved', draftRev: typeof j?.draftRev === 'number' ? j.draftRev : null };
  if (r.status === 401) return { kind: 'auth' };
  if (r.status === 409 && j?.details?.conflict) return { kind: 'conflict' };
  return { kind: 'error', message: typeof j?.error === 'string' ? j.error : null, retry: r.status >= 500 || r.status === 408 || r.status === 429 };
}

/** Pauza înainte de reîncercarea a n-a (0, 1, 2, …): 2 s, 4 s, 8 s … cel mult 60 s. */
export const retryDelay = (attempt: number) => Math.min(60_000, 2000 * 2 ** Math.max(0, attempt));

/** Rezultatele după care salvarea se reîncearcă singură; conflictul și sesiunea expirată așteaptă decizia omului. */
export const shouldRetry = (o: SaveOutcome) => o.kind === 'offline' || (o.kind === 'error' && o.retry);

// ---- ciorna păstrată în sessionStorage cât timp nu s-a putut salva (sesiune expirată, rețea căzută) ----
type Store = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>;
export interface Stash { snapshot: unknown; base: number | null; at: number }
export const stashKey = (id: string) => `casa3d:unsaved:${id}`;
export function stashDraft(store: Store | null | undefined, id: string, snapshot: unknown, base: number | null, now = Date.now()){
  try { store?.setItem(stashKey(id), JSON.stringify({ snapshot, base, at: now })); return true; } catch { return false; }   // plin sau blocat
}
export function readStash(store: Store | null | undefined, id: string): Stash | null {
  try { const raw = store?.getItem(stashKey(id)); if (!raw) return null; const s = JSON.parse(raw);
    if (!s || typeof s !== 'object' || !s.snapshot || typeof s.snapshot !== 'object') return null;
    return { snapshot: s.snapshot, base: Number.isInteger(s.base) ? s.base : null, at: Number(s.at) || 0 }; }
  catch { return null; }
}
export function clearStash(store: Store | null | undefined, id: string){ try { store?.removeItem(stashKey(id)); } catch { /* indisponibil */ } }
/** Ciorna păstrată se trimite cu versiunea de la care pornise (verificarea normală de conflict); fără ea, cu versiunea curentă
 *  a serverului doar dacă serverul nu s-a mișcat — adică nu se trimite niciodată fără `base`. */
export const stashBase = (s: Stash, serverRev: number | null) => s.base ?? serverRev ?? 0;
/** Adresa de login care întoarce în același proiect. */
export const reloginUrl = (path: string) => `/login?next=${encodeURIComponent(path)}`;
