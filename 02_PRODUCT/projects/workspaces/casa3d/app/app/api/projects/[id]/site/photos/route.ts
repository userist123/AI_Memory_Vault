import { handle } from '@/lib/http';
import { addPhoto } from '@/lib/site';
import { readBytesLimited } from '@/lib/body-limit';
import { SITE_LIMITS } from '@/core/site';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
// corpul = octeții pozei (redimensionată în browser); tipul se stabilește din conținut, nu din antet
export const POST = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => addPhoto(o, id, await readBytesLimited(req, SITE_LIMITS.photoBytes)), 201); };
