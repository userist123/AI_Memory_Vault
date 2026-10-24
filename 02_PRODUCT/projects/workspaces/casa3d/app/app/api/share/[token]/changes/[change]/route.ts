import { NextResponse } from 'next/server';
import { decideChange } from '@/lib/change-orders';
import { isShareToken } from '@/lib/share';
import { readJsonLimited } from '@/lib/body-limit';
import { HttpError } from '@/lib/repo';
import { shareRateLimit } from '@/lib/rate-limit';
import { pinRequired, hasPinSession, pinCookies } from '@/lib/share-pin';
import { logError } from '@/lib/log';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ token: string; change: string }> };
const H = { 'cache-control': 'no-store', 'x-robots-tag': 'noindex' };
// Public: decizia clientului pe o modificare trimisă. body: { decision: 'approved' | 'rejected', author, note? }
export const POST = async (req: Request, { params }: Ctx) => { const { token, change } = await params;
  const limited = isShareToken(token) ? shareRateLimit('changes', token, req.headers) : null; if (limited) return limited;
  try { if (!isShareToken(token)) throw new HttpError(404, 'Link inexistent.');
    // aprobarea unei modificări (sumă, zile) e o decizie cu bani: cu PIN activat pe link, cere și sesiunea PIN
    if (await pinRequired(token) && !(await hasPinSession(token, pinCookies(req.headers.get('cookie'))))) throw new HttpError(401, 'Introdu PIN-ul primit de la designer.');
    return NextResponse.json(await decideChange(token, change, await readJsonLimited(req, 4096)), { headers: H }); }
  catch (e: any){ const status = e instanceof HttpError ? e.status : 500; if (status === 500) logError('api/share/[token]/changes/[change]', e); return NextResponse.json({ error: status === 500 ? 'Eroare internă.' : e.message }, { status, headers: H }); } };
