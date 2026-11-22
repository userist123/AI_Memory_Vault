import { NextResponse } from 'next/server';
import { readBriefPhoto } from '@/lib/client-brief';
import { jpegResponse } from '@/lib/client-brief-http';
import { ownerId } from '@/lib/owner';
import { HttpError } from '@/lib/repo';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string; photo: string }> };
// doar proprietarul proiectului; poza clientului nu se păstrează în niciun cache
export const GET = async (_: Request, { params }: Ctx) => { const { id, photo } = await params;
  try { const o = await ownerId(false); if (!o) throw new HttpError(404, 'Nu există.'); return jpegResponse((await readBriefPhoto(o, id, photo)).data); }
  catch (e: any){ return NextResponse.json({ error: 'Nu există.' }, { status: e instanceof HttpError ? e.status : 500, headers: { 'cache-control': 'no-store' } }); } };
