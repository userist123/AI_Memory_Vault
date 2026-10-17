import { handle, body } from '@/lib/http';
import { getOrders, setOrderState } from '@/lib/procurement';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
export const GET = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(o => getOrders(o, id)); };
// body: { key, state: { status, ref?, orderedAt?, eta?, deliveredAt?, note? } }
export const PUT = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => { const b = await body(req); return setOrderState(o, id, b?.key, b?.state); }); };
