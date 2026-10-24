// Răspunsurile rutelor publice ale linkului clientului pentru PIN și pini: fără cache, fără indexare, erori fără detalii
// (PIN-ul nu apare niciodată în erori sau în jurnal).
import { NextResponse } from 'next/server';
import { HttpError } from './repo';
import { PinLockedError } from './share-pin';
import { isLocalRequest } from './public-gate';
export const PUBLIC_HEADERS = { 'cache-control': 'no-store', 'x-robots-tag': 'noindex' } as const;
/** Corpul trebuie să fie JSON (un formular străin nu poate trimite application/json fără preflight CORS). */
export const isJsonRequest = (req: Request) => /^application\/json(;|$)/i.test(req.headers.get('content-type') ?? '');
export const publicError = (status: number, error: string, extra: Record<string, string> = {}) => NextResponse.json({ error }, { status, headers: { ...PUBLIC_HEADERS, ...extra } });
export function publicFail(e: unknown){
  if (e instanceof PinLockedError) return publicError(429, e.message, { 'retry-after': String(Math.ceil(e.retryAfterMs / 1000)) });
  if (e instanceof HttpError) return publicError(e.status, e.message);
  console.error('share pin: eroare internă', e instanceof Error ? e.name : typeof e); return publicError(500, 'Eroare internă.');
}
/** Cererea a venit pe https (direct sau prin tunel): cookie-ul primește Secure. */
export const isHttps = (req: Request) => { try { if (new URL(req.url).protocol === 'https:') return true; } catch { /* adresă relativă */ }
  return (req.headers.get('x-forwarded-proto') ?? '').split(',')[0]!.trim().toLowerCase() === 'https'; };
/** Cookie-ul primește Secure pe https sau când cererea nu e de pe acest calculator (a venit prin tunel, care e pe https):
 *  nu depinde de x-forwarded-proto. Un antet fals poate doar adăuga Secure, niciodată să-l scoată pentru o cerere din tunel. */
export const wantsSecureCookie = (req: Request) => { if (isHttps(req)) return true;
  let host = req.headers.get('host'); if (!host) try { host = new URL(req.url).host; } catch { host = null; }
  return !isLocalRequest(host, req.headers); };
