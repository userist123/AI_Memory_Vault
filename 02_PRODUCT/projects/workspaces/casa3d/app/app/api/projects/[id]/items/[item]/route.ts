import { handle } from '@/lib/http';
import { updateCustomItem, deleteCustomItem } from '@/lib/custom-items';
import { readJsonLimited } from '@/lib/body-limit';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string; item: string }> };
export const PATCH = async (req: Request, { params }: Ctx) => { const { id, item } = await params; return handle(async o => updateCustomItem(o, id, item, await readJsonLimited(req, 8192))); };
export const DELETE = async (_: Request, { params }: Ctx) => { const { id, item } = await params; return handle(o => deleteCustomItem(o, id, item)); };
