import { NextResponse } from 'next/server';
import { addFeedback, addApprovals, listFeedbackForShare } from '@/lib/feedback';
import { isShareToken } from '@/lib/share';
import { readJsonLimited } from '@/lib/body-limit';
import { HttpError } from '@/lib/repo';
import { shareRateLimit } from '@/lib/rate-limit';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ token: string }> };
const H = { 'cache-control': 'no-store', 'x-robots-tag': 'noindex' }, MAX_BODY = 16 * 1024;
// Public, fără cookie de proprietar: doar cu tokenul unui link activ. Erorile nu spun dacă linkul a existat.
async function pub<T>(fn: () => Promise<T>, status = 200){
  try { return NextResponse.json(await fn(), { status, headers: H }); }
  catch (e: any){ if (e instanceof HttpError) return NextResponse.json({ error: e.message }, { status: e.status, headers: H }); console.error(e); return NextResponse.json({ error: 'Eroare internă.' }, { status: 500, headers: H }); } }
export const GET = async (_: Request, { params }: Ctx) => { const { token } = await params; return pub(() => listFeedbackForShare(token)); };
// body: { roomId: string | null, verdict: 'like' | 'change' | 'question', author, text } sau { roomId, author, items: [{ key }] };
// tokenul se verifică înainte de a citi corpul
export const POST = async (req: Request, { params }: Ctx) => { const { token } = await params;
  const limited = isShareToken(token) ? shareRateLimit('feedback', token, req.headers) : null; if (limited) return limited;
  return pub(async () => { if (!isShareToken(token)) throw new HttpError(404, 'Link inexistent.'); const body: any = await readJsonLimited(req, MAX_BODY);
    // { roomId, author, items: [{ key }] } = „Aprobă camera” (mai multe aprobări într-o cerere); altfel o singură părere
    return body && typeof body === 'object' && Array.isArray(body.items) ? addApprovals(token, body) : addFeedback(token, body); }, 201); };
