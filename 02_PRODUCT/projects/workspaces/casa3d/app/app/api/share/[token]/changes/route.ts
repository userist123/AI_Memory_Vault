import { NextResponse } from 'next/server';
import { listChangesForShare } from '@/lib/change-orders';
import { HttpError } from '@/lib/repo';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ token: string }> };
const H = { 'cache-control': 'no-store', 'x-robots-tag': 'noindex' };
// Public, doar cu tokenul unui link activ: modificările trimise clientului (fără ciorne).
export const GET = async (_: Request, { params }: Ctx) => { const { token } = await params;
  try { return NextResponse.json(await listChangesForShare(token), { headers: H }); }
  catch (e: any){ const status = e instanceof HttpError ? e.status : 500; if (status === 500) console.error(e); return NextResponse.json({ error: status === 500 ? 'Eroare internă.' : e.message }, { status, headers: H }); } };
