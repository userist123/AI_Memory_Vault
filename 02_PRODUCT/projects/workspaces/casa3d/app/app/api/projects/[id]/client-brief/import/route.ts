import { handle, body } from '@/lib/http';
import { importClientBrief } from '@/lib/client-brief';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
// body: { fields: string[], rev } — „Preia în brief”: câmpurile bifate și versiunea proiectului văzută de designer (409 la conflict)
export const POST = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => importClientBrief(o, id, await body(req, 4096))); };
