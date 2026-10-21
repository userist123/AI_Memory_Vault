import { NextResponse } from 'next/server';
import { isShareToken } from '@/lib/share';
import { readJsonLimited } from '@/lib/body-limit';
import { shareRateLimit } from '@/lib/rate-limit';
import { verifySharePin, PIN_COOKIE, PIN_SESSION_MS } from '@/lib/share-pin';
import { PUBLIC_HEADERS, isJsonRequest, publicError, publicFail, isHttps } from '@/lib/share-public-http';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ token: string }> };
const MAX_BODY = 4 * 1024;
// Public: clientul introduce PIN-ul primit de la designer. body: { pin: '123456' }. La succes, cookie-ul de sesiune
// (HttpOnly, SameSite=Strict, doar pe calea acestui link, 12 ore). Tokenul se verifică înainte de a citi corpul.
export const POST = async (req: Request, { params }: Ctx) => { const { token } = await params;
  if (!isShareToken(token)) return publicError(404, 'Link inexistent.');
  const limited = shareRateLimit('pin', token, req.headers); if (limited) return limited;
  if (!isJsonRequest(req)) return publicError(415, 'Cerere invalidă.');
  try { const b: any = await readJsonLimited(req, MAX_BODY);
    const r = await verifySharePin(token, b && typeof b === 'object' ? b.pin : undefined);
    const res = NextResponse.json({ ok: true, expiresAt: r.expiresAt }, { headers: PUBLIC_HEADERS });
    res.headers.append('set-cookie', `${PIN_COOKIE}=${r.cookie}; Path=/api/share/${token}; Max-Age=${PIN_SESSION_MS / 1000}; HttpOnly; SameSite=Strict${isHttps(req) ? '; Secure' : ''}`);
    return res; }
  catch (e){ return publicFail(e); } };
