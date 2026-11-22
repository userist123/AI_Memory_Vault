import { NextResponse } from 'next/server';
import { handle, body } from '@/lib/http';
import { HttpError } from '@/lib/repo';
import { ownerId } from '@/lib/owner';
import { logError } from '@/lib/log';
import { getAcceptance, withdrawAcceptance, confirmationFile } from '@/lib/offer-acceptance';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string; acc: string }> };
// Doar proprietarul. GET: acceptarea cu instantaneul ei înghețat; ?download=1 → confirmarea ca fișier text.
export const GET = async (req: Request, { params }: Ctx) => { const { id, acc } = await params;
  if (new URL(req.url).searchParams.get('download') !== '1') return handle(o => getAcceptance(o, id, acc));
  try { const { filename, text } = confirmationFile(await getAcceptance((await ownerId(true))!, id, acc));
    return new Response(text, { status: 200, headers: { 'content-type': 'text/plain; charset=utf-8', 'content-disposition': `attachment; filename="${filename}"`, 'cache-control': 'no-store' } }); }
  catch (e: any){ if (e instanceof HttpError) return NextResponse.json({ error: e.message, details: e.details ?? null }, { status: e.status });
    logError('api/projects/[id]/acceptance/[acc]', e); return NextResponse.json({ error: 'Eroare internă.' }, { status: 500 }); } };
// PATCH body: { action: 'withdraw', date: 'AAAA-LL-ZZ' } — clientul s-a retras; acceptarea rămâne în istoric.
export const PATCH = async (req: Request, { params }: Ctx) => { const { id, acc } = await params;
  return handle(async o => { const b = await body(req, 1024); if (b?.action !== 'withdraw') throw new HttpError(400, 'Acțiune necunoscută.'); return withdrawAcceptance(o, id, acc, b.date); }); };
