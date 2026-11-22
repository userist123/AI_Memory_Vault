import { getPublicBrief, savePublicBrief, BRIEF_LIMITS } from '@/lib/client-brief';
import { briefPub, briefGuard } from '@/lib/client-brief-http';
import { readJsonLimited } from '@/lib/body-limit';
import { isJsonRequest } from '@/lib/share-public-http';
import { HttpError } from '@/lib/repo';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ token: string }> };
// Public, fără cookie de proprietar: doar cu tokenul unui link activ. După trimitere GET întoarce doar { submitted: true }.
export const GET = async (_: Request, { params }: Ctx) => { const { token } = await params; return briefPub(() => getPublicBrief(token)); };
// body: { rev, data } — documentul întreg al chestionarului (ciornă) și versiunea de la care a pornit formularul
export const PUT = async (req: Request, { params }: Ctx) => { const { token } = await params;
  const stop = briefGuard('briefSave', token, req); if (stop) return stop;
  return briefPub(async () => { if (!isJsonRequest(req)) throw new HttpError(415, 'Cerere invalidă.'); return savePublicBrief(token, await readJsonLimited(req, BRIEF_LIMITS.bodyBytes)); }); };
