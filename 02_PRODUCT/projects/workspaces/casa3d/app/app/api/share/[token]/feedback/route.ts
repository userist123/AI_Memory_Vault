import { NextResponse } from 'next/server';
import { addFeedback, listFeedbackForShare } from '@/lib/feedback';
import { HttpError } from '@/lib/repo';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ token: string }> };
const H = { 'cache-control': 'no-store', 'x-robots-tag': 'noindex' };
// Public, fără cookie de proprietar: doar cu tokenul unui link activ. Erorile nu spun dacă linkul a existat.
async function pub<T>(fn: () => Promise<T>, status = 200){
  try { return NextResponse.json(await fn(), { status, headers: H }); }
  catch (e: any){ if (e instanceof HttpError) return NextResponse.json({ error: e.message }, { status: e.status, headers: H }); console.error(e); return NextResponse.json({ error: 'Eroare internă.' }, { status: 500, headers: H }); } }
export const GET = async (_: Request, { params }: Ctx) => { const { token } = await params; return pub(() => listFeedbackForShare(token)); };
// body: { roomId: string | null, verdict: 'like' | 'change' | 'question', author, text }
export const POST = async (req: Request, { params }: Ctx) => { const { token } = await params;
  const len = Number(req.headers.get('content-length') ?? 0); if (len > 16 * 1024) return NextResponse.json({ error: 'Cerere prea mare.' }, { status: 413, headers: H });
  return pub(async () => { let b: unknown; try { b = await req.json(); } catch { throw new HttpError(400, 'Cerere invalidă.'); } return addFeedback(token, b); }, 201); };
