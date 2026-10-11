import { handle } from '@/lib/http';
import { listChanges, addChange } from '@/lib/change-orders';
import { readJsonLimited } from '@/lib/body-limit';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
export const GET = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(o => listChanges(o, id)); };
// body: { title, body?, amount (lei, cu semn), days? }
export const POST = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => addChange(o, id, await readJsonLimited(req, 8192)), 201); };
