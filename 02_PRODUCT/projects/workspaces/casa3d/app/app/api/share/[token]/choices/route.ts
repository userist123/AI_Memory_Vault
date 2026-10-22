import { NextResponse } from 'next/server';
import { chooseOption, listChoicesForShare } from '@/lib/room-choices';
import { isShareToken } from '@/lib/share';
import { readJsonLimited } from '@/lib/body-limit';
import { HttpError } from '@/lib/repo';
import { shareRateLimit } from '@/lib/rate-limit';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ token: string }> };
const H = { 'cache-control': 'no-store', 'x-robots-tag': 'noindex' };
async function pub<T>(fn: () => Promise<T>){
  try { return NextResponse.json(await fn(), { headers: H }); }
  catch (e: any){ if (e instanceof HttpError) return NextResponse.json({ error: e.message }, { status: e.status, headers: H }); console.error(e); return NextResponse.json({ error: 'Eroare internă.' }, { status: 500, headers: H }); } }
// Public, doar cu tokenul unui link activ: alegerile clientului între variantele camerelor.
export const GET = async (_: Request, { params }: Ctx) => { const { token } = await params; return pub(() => listChoicesForShare(token)); };
// body: { roomId, optionId, author, note? }
export const POST = async (req: Request, { params }: Ctx) => { const { token } = await params;
  const limited = isShareToken(token) ? shareRateLimit('choices', token, req.headers) : null; if (limited) return limited;
  return pub(async () => { if (!isShareToken(token)) throw new HttpError(404, 'Link inexistent.'); return chooseOption(token, await readJsonLimited(req, 4096)); }); };
