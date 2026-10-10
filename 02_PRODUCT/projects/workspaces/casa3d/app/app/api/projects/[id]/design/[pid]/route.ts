import { handle, body } from '@/lib/http';
import { HttpError } from '@/lib/repo';
import * as design from '@/lib/design';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string; pid: string }> };
// body: { index: 0|1|2, action: 'apply' | 'reject', confirmWarnings? }
export const POST = async (req: Request, { params }: Ctx) => { const { id, pid } = await params; return handle(async o => { const b = await body(req);
  if (b.action !== 'apply' && b.action !== 'reject') throw new HttpError(400, 'Acțiune necunoscută: folosește apply sau reject.');
  if (!Number.isInteger(b.index)) throw new HttpError(400, 'Varianta lipsește.');
  return design.decideDesign(o, id, pid, b.index, b.action, b.confirmWarnings === true); }); };
