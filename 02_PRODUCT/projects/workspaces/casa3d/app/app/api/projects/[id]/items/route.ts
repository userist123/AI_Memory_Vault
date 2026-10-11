import { handle } from '@/lib/http';
import { addCustomItem } from '@/lib/custom-items';
import { readJsonLimited } from '@/lib/body-limit';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
// body: { name, supplier, url?, image?, sku?, price?, currency?, qty, unit?, roomId? }
export const POST = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => addCustomItem(o, id, await readJsonLimited(req, 8192)), 201); };
