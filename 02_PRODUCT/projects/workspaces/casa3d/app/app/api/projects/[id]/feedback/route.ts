import { handle, body } from '@/lib/http';
import { listFeedback, setFeedbackResolved } from '@/lib/feedback';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
export const GET = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(o => listFeedback(o, id)); };
// body: { id, resolved: boolean }
export const PATCH = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => { const b = await body(req); return setFeedbackResolved(o, id, b?.id, b?.resolved); }); };
