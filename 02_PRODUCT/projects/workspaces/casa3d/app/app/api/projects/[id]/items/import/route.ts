import { handle } from '@/lib/http';
import { importCustomItems } from '@/lib/custom-items';
import { readJsonLimited } from '@/lib/body-limit';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
// body: { csv: string, dryRun?: boolean } — max 200 KB de CSV (JSON-ul își păstrează o rezervă pentru escape-uri)
export const POST = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => importCustomItems(o, id, await readJsonLimited(req, 700 * 1024))); };
