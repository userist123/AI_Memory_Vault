import { handle } from '@/lib/http';
import { updateChange, deleteChange } from '@/lib/change-orders';
import { readJsonLimited } from '@/lib/body-limit';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string; change: string }> };
// body: câmpuri de ciornă sau { action: 'send' | 'withdraw' }
export const PATCH = async (req: Request, { params }: Ctx) => { const { id, change } = await params; return handle(async o => updateChange(o, id, change, await readJsonLimited(req, 8192))); };
export const DELETE = async (_: Request, { params }: Ctx) => { const { id, change } = await params; return handle(o => deleteChange(o, id, change)); };
