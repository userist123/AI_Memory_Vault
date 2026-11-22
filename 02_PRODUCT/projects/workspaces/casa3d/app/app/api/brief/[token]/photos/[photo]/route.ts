import { readPublicPhoto, patchPublicPhoto, deletePublicPhoto } from '@/lib/client-brief';
import { briefPub, briefGuard, briefNotFound, jpegResponse } from '@/lib/client-brief-http';
import { readJsonLimited } from '@/lib/body-limit';
import { isJsonRequest } from '@/lib/share-public-http';
import { HttpError } from '@/lib/repo';
import { logError } from '@/lib/log';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ token: string; photo: string }> };
// clientul își vede propria poză doar cât linkul e activ și chestionarul netrimis; altfel același 404
export const GET = async (_: Request, { params }: Ctx) => { const { token, photo } = await params;
  try { return jpegResponse((await readPublicPhoto(token, photo)).data); }
  catch (e){ if (!(e instanceof HttpError)) logError('api/brief/photos', e); return briefNotFound(); } };
// body: { verdict?: 'love' | 'ok' | 'dislike' | null, note?: string }
export const PATCH = async (req: Request, { params }: Ctx) => { const { token, photo } = await params;
  const stop = briefGuard('briefSave', token, req); if (stop) return stop;
  return briefPub(async () => { if (!isJsonRequest(req)) throw new HttpError(415, 'Cerere invalidă.'); return patchPublicPhoto(token, photo, await readJsonLimited(req, 2048)); }); };
export const DELETE = async (req: Request, { params }: Ctx) => { const { token, photo } = await params;
  const stop = briefGuard('briefSave', token, req); if (stop) return stop;
  return briefPub(() => deletePublicPhoto(token, photo)); };
