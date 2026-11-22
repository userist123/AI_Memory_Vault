import { handle } from '@/lib/http';
import { listSiteLog, addSiteLog } from '@/lib/site-log';
import { readJsonLimited } from '@/lib/body-limit';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
// Jurnalul de șantier, doar pentru proprietar (cu tot cu intrările interne).
export const GET = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(o => listSiteLog(o, id)); };
// body: { date, text, weather?, workers?, trades?, photos?, clientVisible? } — clientVisible lipsă = intern
export const POST = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => addSiteLog(o, id, await readJsonLimited(req, 16 * 1024)), 201); };
