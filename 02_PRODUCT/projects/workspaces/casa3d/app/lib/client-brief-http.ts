// Răspunsurile rutelor publice ale chestionarului clientului: fără cache, fără indexare, erori fără detalii interne.
// Limita de cereri se aplică doar după ce tokenul are forma corectă (ca la rutele /api/share).
import { NextResponse } from 'next/server';
import { HttpError } from './repo';
import { isShareToken } from './share';
import { shareRateLimit, type ShareAction } from './rate-limit';
import { PUBLIC_HEADERS } from './share-public-http';
import { logError } from './log';
/** `details` ajunge la client doar pentru semnalele formularului (conflict, trimis, limită, întrebări lipsă). */
export async function briefPub<T>(fn: () => Promise<T>, status = 200){
  try { return NextResponse.json(await fn(), { status, headers: PUBLIC_HEADERS }); }
  catch (e: any){ if (e instanceof HttpError) return NextResponse.json({ error: e.message, ...(e.details && typeof e.details === 'object' ? e.details : {}) }, { status: e.status, headers: PUBLIC_HEADERS });
    logError('api/brief', e); return NextResponse.json({ error: 'Eroare internă.' }, { status: 500, headers: PUBLIC_HEADERS }); }
}
export const briefNotFound = () => NextResponse.json({ error: 'Link inexistent.' }, { status: 404, headers: PUBLIC_HEADERS });
/** null dacă cererea poate continua; altfel răspunsul (404 pentru un token cu formă greșită, 429 peste limită). */
export function briefGuard(action: ShareAction, token: string, req: Request){ return isShareToken(token) ? shareRateLimit(action, token, req.headers) : briefNotFound(); }
/** Poza servită ca fișier: tipul fix, fără ghicire, fără cache, fără scripturi. */
export const jpegResponse = (data: Uint8Array) => new NextResponse(Buffer.from(data), { headers: { 'content-type': 'image/jpeg', 'x-content-type-options': 'nosniff', 'content-security-policy': "default-src 'none'; sandbox",
  'cache-control': 'private, no-store', 'x-robots-tag': 'noindex', 'content-disposition': 'inline' } });
