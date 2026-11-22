// Cursul BNR la cerere („Preia cursul BNR”): serverul citește fișierul public al Băncii Naționale și întoarce data și cursurile.
// Adresa e fixă (nimic din cerere nu ajunge în ea), fără redirecționări, cu termen-limită și mărime limitată; cererea nu
// poartă nimic despre proiect sau utilizator (fără cookie, fără referer, User-Agent generic). Răspunsul se ține ~1 oră în memorie.
import { HttpError } from './repo';
import { parseBnrXml, FxError, type BnrRates } from '../core/fx';

/** Adresa oficială a fișierului zilnic; a doua e adresa veche (din 2026 redirecționează spre prima pagină, rămâne ca rezervă). */
export const BNR_URLS = ['https://curs.bnr.ro/nbrfxrates.xml', 'https://www.bnr.ro/nbrfxrates.xml'] as const;
export const BNR_LIMITS = { timeoutMs: 5000, bytes: 200 * 1024, cacheMs: 60 * 60 * 1000, failCacheMs: 60 * 1000 };
export interface BnrResult extends BnrRates { fetchedAt: string }
type FetchFn = (url: string, init: RequestInit) => Promise<Response>;

let cache: { at: number; data: BnrResult } | null = null, failedAt = 0;
/** Doar pentru teste: golește memoria. */
export function resetBnrCache(){ cache = null; failedAt = 0; }

/** Corpul ca text, citit pe bucăți; peste limită se oprește (nu se încarcă un răspuns uriaș în memorie). */
async function readLimited(res: Response, max: number): Promise<string> {
  const len = Number(res.headers.get('content-length') ?? 0); if (len > max) throw new FxError('Răspunsul BNR e prea mare.');
  if (!res.body) { const t = await res.text(); if (Buffer.byteLength(t, 'utf8') > max) throw new FxError('Răspunsul BNR e prea mare.'); return t; }
  const reader = res.body.getReader(), chunks: Uint8Array[] = []; let n = 0;
  for (;;){ const { done, value } = await reader.read(); if (done) break;
    n += value.byteLength; if (n > max){ await reader.cancel().catch(() => {}); throw new FxError('Răspunsul BNR e prea mare.'); }
    chunks.push(value); }
  return Buffer.concat(chunks).toString('utf8');
}
async function fetchOne(url: string, fetchImpl: FetchFn): Promise<BnrRates> {
  const ctl = new AbortController(), timer = setTimeout(() => ctl.abort(), BNR_LIMITS.timeoutMs);
  try {
    const res = await fetchImpl(url, { method: 'GET', redirect: 'manual', signal: ctl.signal, credentials: 'omit', referrerPolicy: 'no-referrer', cache: 'no-store',
      headers: { 'user-agent': 'Mozilla/5.0 (compatible; fx-rates/1.0)', accept: 'application/xml,text/xml' } });
    if (res.status !== 200) throw new FxError(`BNR a răspuns ${res.status}.`);
    return parseBnrXml(await readLimited(res, BNR_LIMITS.bytes));
  } finally { clearTimeout(timer); }
}

/**
 * Cursurile BNR ale zilei (din memorie dacă au mai puțin de o oră). La eșec: 502 cu mesaj clar — cursul se poate scrie de mână;
 * după un eșec nu se mai încearcă un minut (un buton apăsat repetat nu bombardează BNR).
 */
export async function bnrRates(fetchImpl: FetchFn = fetch as FetchFn, now: () => number = Date.now): Promise<BnrResult> {
  const t = now(); if (cache && t - cache.at < BNR_LIMITS.cacheMs) return cache.data;
  const fail = () => new HttpError(502, 'Cursul BNR nu a putut fi citit acum. Introdu cursul de mână sau încearcă mai târziu.');
  if (failedAt && t - failedAt < BNR_LIMITS.failCacheMs) throw fail();
  for (const url of BNR_URLS){
    try { const r = await fetchOne(url, fetchImpl), data: BnrResult = { ...r, fetchedAt: new Date(t).toISOString() }; cache = { at: t, data }; failedAt = 0; return data; }
    catch { /* următoarea adresă; motivul exact nu ajunge la utilizator */ }
  }
  failedAt = t; throw fail();
}
