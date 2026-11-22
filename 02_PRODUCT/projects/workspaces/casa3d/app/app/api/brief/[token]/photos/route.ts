import { addPublicPhoto, BRIEF_LIMITS } from '@/lib/client-brief';
import { briefPub, briefGuard } from '@/lib/client-brief-http';
import { readBytesLimited } from '@/lib/body-limit';
import { HttpError } from '@/lib/repo';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ token: string }> };
// corpul = octeții pozei (JPEG, micșorată în browser); se păstrează doar imaginea reconstruită, fără metadate
export const POST = async (req: Request, { params }: Ctx) => { const { token } = await params;
  const stop = briefGuard('briefPhoto', token, req); if (stop) return stop;
  return briefPub(async () => { if (!/^image\/jpeg(;|$)/i.test(req.headers.get('content-type') ?? '')) throw new HttpError(415, 'Sunt acceptate doar poze JPEG.');
    return addPublicPhoto(token, await readBytesLimited(req, BRIEF_LIMITS.photoBytes)); }, 201); };
