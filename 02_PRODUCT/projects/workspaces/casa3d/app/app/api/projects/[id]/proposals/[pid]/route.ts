import { handle, body } from '@/lib/http';
import * as repo from '@/lib/repo';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string; pid: string }> };
// body: { tier, action: 'apply' | 'reject', confirmWarnings? }
export const POST = async (req: Request, { params }: Ctx) => { const { id, pid } = await params; return handle(async o => { const b = await body(req);
  return b.action === 'reject' ? repo.rejectProposal(o, id, pid, b.tier) : repo.applyProposal(o, id, pid, b.tier, !!b.confirmWarnings); }); };
