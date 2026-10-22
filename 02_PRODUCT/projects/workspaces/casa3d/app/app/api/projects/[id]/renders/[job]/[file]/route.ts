import { NextResponse } from 'next/server';
import { HttpError } from '@/lib/repo';
import { ownerId } from '@/lib/owner';
import { readRender } from '@/lib/renders';
export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';
type Ctx = { params: Promise<{ id: string; job: string; file: string }> };
export const GET = async (_req: Request, { params }: Ctx) => { try { const { id, job, file } = await params, buf = await readRender((await ownerId(true))!, id, job, file);
    return new Response(new Uint8Array(buf), { headers: { 'content-type': 'image/png', 'cache-control': 'private, max-age=31536000, immutable' } }); }
  catch (e: any){ if (e instanceof HttpError) return NextResponse.json({ error: e.message }, { status: e.status }); console.error(e); return NextResponse.json({ error: 'Eroare internă.' }, { status: 500 }); } };
