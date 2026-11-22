import { handle, body } from '@/lib/http';
import * as rfq from '@/lib/rfq';
import { HttpError } from '@/lib/repo';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
// Cererile de ofertă către meseriași, pentru designer (doar proprietarul proiectului): cererile cu invitațiile și ofertele
// trimise, rândurile de manoperă disponibile acum și prețurile contractate
export const GET = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(o => rfq.getRfq(o, id)); };
// body: { title, trades, note?, siteCity?, dueDate? } — cerere nouă; lista de cantități se îngheață acum
export const POST = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => rfq.createRfq(o, id, await body(req, 8192)), 201); };
// body: { action: 'clearContract', rev } — scoate din proiect toate prețurile contractate
export const PATCH = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => { const b = await body(req, 1024);
  if (b?.action === 'clearContract') return rfq.clearLaborContract(o, id, b); throw new HttpError(400, 'Acțiune necunoscută.'); }); };
