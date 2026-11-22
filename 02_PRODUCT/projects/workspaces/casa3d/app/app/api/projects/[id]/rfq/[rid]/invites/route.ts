import { handle, body } from '@/lib/http';
import { addInvite } from '@/lib/rfq';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string; rid: string }> };
// body: { label, expiresInDays? } — invitație nouă: un link propriu pentru un meseriaș
export const POST = async (req: Request, { params }: Ctx) => { const { id, rid } = await params; return handle(async o => addInvite(o, id, rid, await body(req, 1024)), 201); };
