import { getPublicOffer, savePublicOffer, RFQ_MAX } from '@/lib/rfq';
import { briefPub, briefGuard } from '@/lib/client-brief-http';
import { readJsonLimited } from '@/lib/body-limit';
import { isJsonRequest } from '@/lib/share-public-http';
import { HttpError } from '@/lib/repo';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ token: string }> };
// Public, fără cookie de proprietar: doar cu tokenul unui link activ. Întoarce cererea și oferta proprie a meseriașului
// (după trimitere, read-only); nimic despre client, buget sau celelalte oferte.
export const GET = async (_: Request, { params }: Ctx) => { const { token } = await params; return briefPub(() => getPublicOffer(token)); };
// body: { rev, data } — oferta întreagă (ciornă) și versiunea de la care a pornit formularul
export const PUT = async (req: Request, { params }: Ctx) => { const { token } = await params;
  const stop = briefGuard('rfqSave', token, req); if (stop) return stop;
  return briefPub(async () => { if (!isJsonRequest(req)) throw new HttpError(415, 'Cerere invalidă.'); return savePublicOffer(token, await readJsonLimited(req, RFQ_MAX.bodyBytes)); }); };
