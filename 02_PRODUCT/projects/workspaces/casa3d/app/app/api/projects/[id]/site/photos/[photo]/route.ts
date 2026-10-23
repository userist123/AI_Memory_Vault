import { NextResponse } from 'next/server';
import { readPhoto } from '@/lib/site';
import { ownerId } from '@/lib/owner';
import { HttpError } from '@/lib/repo';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string; photo: string }> };
// doar proprietarul; tipul din conținut, fără ghicire (nosniff), fără scripturi (CSP), păstrată doar local în browser
export const GET = async (_: Request, { params }: Ctx) => { const { id, photo } = await params;
  try { const o = await ownerId(false); if (!o) throw new HttpError(404, 'Nu există.'); const p = await readPhoto(o, id, photo);
    return new NextResponse(Buffer.from(p.data), { headers: { 'content-type': p.mime, 'x-content-type-options': 'nosniff', 'content-security-policy': "default-src 'none'; sandbox", 'cache-control': 'private, max-age=86400' } }); }
  catch (e: any){ return NextResponse.json({ error: 'Nu există.' }, { status: e instanceof HttpError ? e.status : 500 }); } };
