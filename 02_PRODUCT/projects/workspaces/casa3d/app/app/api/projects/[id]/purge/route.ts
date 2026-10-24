import { handle, body } from '@/lib/http';
import { purgeProject } from '@/lib/trash';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
export const POST = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => purgeProject(o, id, (await body(req))?.name)); };
