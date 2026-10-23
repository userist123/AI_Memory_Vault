import { handle } from '@/lib/http';
import { listSnags, addSnag } from '@/lib/site';
import { readJsonLimited } from '@/lib/body-limit';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
export const GET = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(o => listSnags(o, id)); };
// body: { title, body?, roomId?, trade, status?, photos? }
export const POST = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => addSnag(o, id, await readJsonLimited(req, 16 * 1024)), 201); };
