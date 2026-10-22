import { NextResponse } from 'next/server';
import { handle, body } from '@/lib/http';
import { setSharePin, clearSharePin } from '@/lib/share-pin';
import { sameOriginJson } from '@/lib/public-gate';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
// Doar proprietarul (rămâne în afara porții publice). body: { token }. POST → PIN nou (arătat o singură dată), DELETE → pini dezactivați.
// Numai din aplicația însăși (o pagină străină nu poate roti PIN-ul), iar răspunsul cu PIN-ul nu se păstrează în niciun cache.
const H = { 'cache-control': 'no-store' };
const refused = () => NextResponse.json({ error: 'Cerere refuzată.' }, { status: 403, headers: H });
const noStore = (r: NextResponse) => { r.headers.set('cache-control', 'no-store'); return r; };
export const POST = async (req: Request, { params }: Ctx) => { if (!sameOriginJson(req.headers)) return refused(); const { id } = await params;
  return noStore(await handle(async o => setSharePin(o, id, (await body(req))?.token))); };
export const DELETE = async (req: Request, { params }: Ctx) => { if (!sameOriginJson(req.headers)) return refused(); const { id } = await params;
  return noStore(await handle(async o => clearSharePin(o, id, (await body(req))?.token))); };
