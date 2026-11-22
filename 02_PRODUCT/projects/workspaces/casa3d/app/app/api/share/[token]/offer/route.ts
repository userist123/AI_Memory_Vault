import { NextResponse } from 'next/server';
import { isShareToken } from '@/lib/share';
import { readJsonLimited } from '@/lib/body-limit';
import { shareRateLimit } from '@/lib/rate-limit';
import { pinCookies } from '@/lib/share-pin';
import { offerForShare, acceptOffer } from '@/lib/offer-acceptance';
import { PUBLIC_HEADERS, isJsonRequest, publicError, publicFail } from '@/lib/share-public-http';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ token: string }> };
const MAX_BODY = 4 * 1024;
// Public: oferta de acceptat pe linkul clientului (lib/offer-acceptance.ts). 404 dacă designerul nu a activat acceptarea pe
// acest link (sau linkul e revocat / expirat / proiectul e în coș), 401 fără sesiunea PIN a linkului.
export const GET = async (req: Request, { params }: Ctx) => { const { token } = await params;
  if (!isShareToken(token)) return publicError(404, 'Link inexistent.');
  const limited = shareRateLimit('offerView', token, req.headers); if (limited) return limited;
  try { return NextResponse.json(await offerForShare(token, pinCookies(req.headers.get('cookie'))), { headers: PUBLIC_HEADERS }); }
  catch (e){ return publicFail(e); } };
// Public: clientul acceptă oferta. body: { hash, name, contact?, onBehalf?, accept: true, earlyStart?: boolean }.
// Scrierile de pe altă origine sunt oprite în proxy.ts (CSRF); aici corpul trebuie să fie JSON, iar tokenul se verifică
// înainte de a citi corpul. 201 la o acceptare nouă, 200 dacă aceeași versiune era deja acceptată (idempotent),
// 409 dacă oferta s-a schimbat între afișare și clic.
export const POST = async (req: Request, { params }: Ctx) => { const { token } = await params;
  if (!isShareToken(token)) return publicError(404, 'Link inexistent.');
  const limited = shareRateLimit('offerAccept', token, req.headers); if (limited) return limited;
  if (!isJsonRequest(req)) return publicError(415, 'Cerere invalidă.');
  try { const r = await acceptOffer(token, pinCookies(req.headers.get('cookie')), await readJsonLimited(req, MAX_BODY));
    return NextResponse.json(r, { status: r.created ? 201 : 200, headers: PUBLIC_HEADERS }); }
  catch (e){ return publicFail(e); } };
