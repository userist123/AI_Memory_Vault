import { handle } from '@/lib/http';
import { updateSnag, deleteSnag } from '@/lib/site';
import { readJsonLimited } from '@/lib/body-limit';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string; snag: string }> };
export const PATCH = async (req: Request, { params }: Ctx) => { const { id, snag } = await params; return handle(async o => updateSnag(o, id, snag, await readJsonLimited(req, 16 * 1024))); };
export const DELETE = async (_: Request, { params }: Ctx) => { const { id, snag } = await params; return handle(o => deleteSnag(o, id, snag)); };
