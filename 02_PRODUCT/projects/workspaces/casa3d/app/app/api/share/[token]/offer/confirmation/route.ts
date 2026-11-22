import { isShareToken } from '@/lib/share';
import { shareRateLimit } from '@/lib/rate-limit';
import { pinCookies } from '@/lib/share-pin';
import { confirmationForShare } from '@/lib/offer-acceptance';
import { PUBLIC_HEADERS, publicError, publicFail } from '@/lib/share-public-http';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ token: string }> };
// Public, cu sesiunea PIN a linkului: „Confirmarea acceptării” ca fișier text (suport durabil; aplicația nu trimite e-mail).
// ?id=<acceptare> alege o acceptare anume a proiectului; fără el, cea la care se referă starea curentă a ofertei.
export const GET = async (req: Request, { params }: Ctx) => { const { token } = await params;
  if (!isShareToken(token)) return publicError(404, 'Link inexistent.');
  const limited = shareRateLimit('offerView', token, req.headers); if (limited) return limited;
  try { const { filename, text } = await confirmationForShare(token, pinCookies(req.headers.get('cookie')), new URL(req.url).searchParams.get('id'));
    return new Response(text, { status: 200, headers: { ...PUBLIC_HEADERS, 'content-type': 'text/plain; charset=utf-8', 'content-disposition': `attachment; filename="${filename}"` } }); }
  catch (e){ return publicFail(e); } };
