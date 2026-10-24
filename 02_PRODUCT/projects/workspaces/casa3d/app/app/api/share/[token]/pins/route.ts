import { NextResponse } from 'next/server';
import { isShareToken, resolveShare } from '@/lib/share';
import { readJsonLimited } from '@/lib/body-limit';
import { shareRateLimit } from '@/lib/rate-limit';
import { addPinnedFeedback } from '@/lib/feedback';
import { hasPinSession, pinCookies } from '@/lib/share-pin';
import { PUBLIC_HEADERS, isJsonRequest, publicError, publicFail } from '@/lib/share-public-http';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ token: string }> };
const MAX_BODY = 4 * 1024;
// Public, cu sesiunea PIN: un pin pe imagine. body: { verdict, author, text, roomId, anchor: { surface, u, v } }.
// Link necunoscut / revocat → 404 (ca pe celelalte rute), fără sesiune validă → 401 (corpul nu se mai citește).
export const POST = async (req: Request, { params }: Ctx) => { const { token } = await params;
  if (!isShareToken(token)) return publicError(404, 'Link inexistent.');
  const limited = shareRateLimit('feedback', token, req.headers); if (limited) return limited;
  if (!isJsonRequest(req)) return publicError(415, 'Cerere invalidă.');
  try { await resolveShare(token);
    if (!(await hasPinSession(token, pinCookies(req.headers.get('cookie'))))) return publicError(401, 'Introdu PIN-ul primit de la designer.');
    const b = await readJsonLimited(req, MAX_BODY);
    return NextResponse.json(await addPinnedFeedback(token, b), { status: 201, headers: PUBLIC_HEADERS }); }
  catch (e){ return publicFail(e); } };
