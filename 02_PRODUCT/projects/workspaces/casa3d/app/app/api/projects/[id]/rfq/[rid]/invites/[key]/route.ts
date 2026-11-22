import { handle, body } from '@/lib/http';
import * as rfq from '@/lib/rfq';
import { HttpError } from '@/lib/repo';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string; rid: string; key: string }> };
// `key` e numele invitației din starea designerului (nu cheia linkului). body: { action: 'revoke' | 'reopen' }
export const PATCH = async (req: Request, { params }: Ctx) => { const { id, rid, key } = await params; return handle(async o => { const a = (await body(req, 1024))?.action;
  if (a === 'revoke') return rfq.revokeInvite(o, id, rid, key); if (a === 'reopen') return rfq.reopenInvite(o, id, rid, key); throw new HttpError(400, 'Acțiune necunoscută.'); }); };
// șterge invitația cu tot cu oferta ei
export const DELETE = async (_: Request, { params }: Ctx) => { const { id, rid, key } = await params; return handle(o => rfq.deleteInvite(o, id, rid, key)); };
