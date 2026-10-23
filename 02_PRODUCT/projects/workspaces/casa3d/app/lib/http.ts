import { NextResponse } from 'next/server';
import { HttpError } from './repo';
import { ownerId } from './owner';
import { logError } from './log';
import { readJsonLimited } from './body-limit';
export async function handle<T>(fn: (owner: string) => Promise<T>, status = 200){
  try { const owner = (await ownerId(true))!; return NextResponse.json(await fn(owner), { status }); }
  catch (e: any){ if (e instanceof HttpError) return NextResponse.json({ error: e.message, details: e.details ?? null }, { status: e.status });
    logError('lib/http.ts', e); return NextResponse.json({ error: 'Eroare internă.' }, { status: 500 }); }
}
/** Corpul JSON al unei cereri autentificate, cu limită reală de octeți (413 peste): o sesiune validă nu poate umple
 *  memoria serverului cu un corp de câțiva GB. Implicit 512 KB (logo-ul de studio are cel mult 300 KB). */
export const BODY_MAX = 512 * 1024;
/** Draftul proiectului: 1 MB de date + imaginea de calc de cel mult 1,5 MB, codată base64 (×4/3), cu rezervă. */
export const DRAFT_BODY_MAX = 3.5 * 1024 * 1024;
export async function body(req: Request, maxBytes = BODY_MAX): Promise<any> { return readJsonLimited(req, maxBytes); }
