import { handle } from '@/lib/http';
import { addCustomItem, getCustomItems } from '@/lib/custom-items';
import { readJsonLimited } from '@/lib/body-limit';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
export const GET = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(o => getCustomItems(o, id)); };
// body: { name, supplier, url?, image?, sku?, price?, currency?, qty, unit?, roomId?, costPrice?, leadDays?, dimension? }
export const POST = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => addCustomItem(o, id, await readJsonLimited(req, 8192)), 201); };
