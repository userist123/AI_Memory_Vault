import { submitPublicBrief } from '@/lib/client-brief';
import { briefPub, briefGuard } from '@/lib/client-brief-http';
import { readJsonLimited } from '@/lib/body-limit';
import { isJsonRequest } from '@/lib/share-public-http';
import { HttpError } from '@/lib/repo';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ token: string }> };
// body: { rev } — trimite răspunsurile salvate (validare completă); după asta linkul nu mai primește nimic
export const POST = async (req: Request, { params }: Ctx) => { const { token } = await params;
  const stop = briefGuard('briefSave', token, req); if (stop) return stop;
  return briefPub(async () => { if (!isJsonRequest(req)) throw new HttpError(415, 'Cerere invalidă.'); return submitPublicBrief(token, await readJsonLimited(req, 1024)); }); };
