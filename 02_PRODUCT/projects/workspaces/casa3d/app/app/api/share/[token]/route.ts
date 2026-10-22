import { NextResponse } from 'next/server';
import { resolveShare } from '@/lib/share';
import { HttpError } from '@/lib/repo';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ token: string }> };
// Public, fără cookie de proprietar: doar revizia partajată, read-only.
export const GET = async (_: Request, { params }: Ctx) => { const { token } = await params;
  try { return NextResponse.json(await resolveShare(token), { headers: { 'cache-control': 'no-store', 'x-robots-tag': 'noindex' } }); }
  catch (e: any){ const status = e instanceof HttpError ? e.status : 500; return NextResponse.json({ error: status === 404 ? 'Link inexistent.' : 'Eroare internă.' }, { status, headers: { 'x-robots-tag': 'noindex' } }); } };
