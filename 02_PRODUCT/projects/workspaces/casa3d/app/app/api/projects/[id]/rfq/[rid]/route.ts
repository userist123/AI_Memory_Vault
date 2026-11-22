import { handle, body } from '@/lib/http';
import * as rfq from '@/lib/rfq';
import { HttpError } from '@/lib/repo';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string; rid: string }> };
// body: { action: 'award', invite, rev } | { action: 'unaward', rev } — rev = versiunea proiectului văzută de designer (409 la conflict)
export const PATCH = async (req: Request, { params }: Ctx) => { const { id, rid } = await params; return handle(async o => { const b = await body(req, 1024);
  if (b?.action === 'award') return rfq.awardRfq(o, id, rid, b); if (b?.action === 'unaward') return rfq.unawardRfq(o, id, rid, b); throw new HttpError(400, 'Acțiune necunoscută.'); }); };
// șterge cererea cu toate invitațiile și ofertele ei
export const DELETE = async (_: Request, { params }: Ctx) => { const { id, rid } = await params; return handle(o => rfq.deleteRfq(o, id, rid)); };
