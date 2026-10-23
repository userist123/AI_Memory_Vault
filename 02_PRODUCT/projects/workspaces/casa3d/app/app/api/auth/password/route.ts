import { NextResponse } from 'next/server';
import { readAuth, setPassword, removePassword } from '@/lib/auth';
import { isLocalRequest, sameOriginJson } from '@/lib/public-gate';
import { publicBase } from '@/lib/share';
import { readJsonLimited } from '@/lib/body-limit';
import { ownerId } from '@/lib/owner';
import { HttpError } from '@/lib/repo';
export const dynamic = 'force-dynamic';
// Doar de pe calculatorul designerului (și poarta din middleware.ts o blochează prin tunel): starea, setarea, oprirea.
async function local<T>(req: Request, fn: () => Promise<T>){
  try { if (!isLocalRequest(req.headers.get('host'), req.headers)) throw new HttpError(404, 'Nu există.');
    // o pagină străină deschisă în browserul designerului nu poate schimba parola (CSRF): doar din aplicație, ca JSON
    if (req.method !== 'GET' && !sameOriginJson(req.headers, req.method === 'DELETE')) throw new HttpError(403, 'Cerere refuzată.'); return NextResponse.json(await fn(), { headers: { 'cache-control': 'no-store' } }); }
  catch (e: any){ const status = e instanceof HttpError ? e.status : e?.status ?? 500; return NextResponse.json({ error: status === 500 ? 'Eroare internă.' : e.message }, { status }); } }
export const GET = (req: Request) => local(req, async () => { const c = readAuth(); return { enabled: !!c, updatedAt: c?.updatedAt ?? null, publicUrl: publicBase() }; });
// body: { password } — proiectele care se văd de pe telefon sunt ale browserului de pe care pui parola
export const POST = (req: Request) => local(req, async () => { const b = await readJsonLimited(req, 2048) as any; setPassword(b?.password, (await ownerId(true))!); return { enabled: true }; });
export const DELETE = (req: Request) => local(req, async () => { removePassword(); return { enabled: false }; });
