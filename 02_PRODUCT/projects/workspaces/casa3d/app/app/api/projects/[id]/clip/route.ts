import { handle } from '@/lib/http';
import { getProject } from '@/lib/repo';
import { fetchProduct } from '@/lib/clip';
import { readJsonLimited } from '@/lib/body-limit';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
// body: { url } → previzualizarea produsului din pagina magazinului (nu se salvează nimic)
export const POST = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => { await getProject(o, id); const b = await readJsonLimited(req, 4096) as any; return fetchProduct(b?.url); }); };
