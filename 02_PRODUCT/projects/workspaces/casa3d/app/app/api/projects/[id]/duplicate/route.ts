import { handle, body } from '@/lib/http';
import { duplicateProject } from '@/lib/trash';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
export const POST = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => duplicateProject(o, id, (await body(req))?.name), 201); };
