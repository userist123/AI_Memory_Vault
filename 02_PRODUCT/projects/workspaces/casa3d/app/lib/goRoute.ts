import { NextResponse } from 'next/server';
import { go } from './outbound';
import { HttpError } from './repo';
export async function goHandler(req: Request, kind: 'o' | 'm', id: string){
  try { const ref = req.headers.get('referer'); let from: string | null = null; try { if (ref) from = new URL(ref).pathname; } catch {}
    const { url, type } = await go(kind, id, { ua: req.headers.get('user-agent'), from });
    return new NextResponse(null, { status: 302, headers: { location: url, 'cache-control': 'no-store', 'x-robots-tag': 'noindex, nofollow', 'x-link-type': type, 'referrer-policy': 'no-referrer' } }); }
  catch (e: any){ const status = e instanceof HttpError ? e.status : 500; return new NextResponse(e instanceof HttpError ? e.message : 'Eroare.', { status, headers: { 'x-robots-tag': 'noindex', 'content-type': 'text/plain; charset=utf-8' } }); }
}
