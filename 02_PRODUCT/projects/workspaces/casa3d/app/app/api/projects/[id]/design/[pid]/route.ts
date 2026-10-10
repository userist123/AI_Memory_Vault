import { handle, body } from '@/lib/http';
import * as design from '@/lib/design';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string; pid: string }> };
// body: { index: 0|1|2, action: 'apply' | 'reject', confirmWarnings? }
export const POST = async (req: Request, { params }: Ctx) => { const { id, pid } = await params; return handle(async o => { const b = await body(req);
  return design.decideDesign(o, id, pid, Number(b.index), b.action === 'reject' ? 'reject' : 'apply', !!b.confirmWarnings); }); };
