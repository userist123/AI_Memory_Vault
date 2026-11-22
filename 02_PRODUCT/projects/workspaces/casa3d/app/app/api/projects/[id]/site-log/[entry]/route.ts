import { handle } from '@/lib/http';
import { updateSiteLog, deleteSiteLog } from '@/lib/site-log';
import { readJsonLimited } from '@/lib/body-limit';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string; entry: string }> };
// body: doar câmpurile care se schimbă (ex. { clientVisible: true } de la comutator)
export const PUT = async (req: Request, { params }: Ctx) => { const { id, entry } = await params; return handle(async o => updateSiteLog(o, id, entry, await readJsonLimited(req, 16 * 1024))); };
export const DELETE = async (_: Request, { params }: Ctx) => { const { id, entry } = await params; return handle(o => deleteSiteLog(o, id, entry)); };
