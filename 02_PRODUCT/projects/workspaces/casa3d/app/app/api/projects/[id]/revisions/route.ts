import { handle, body } from '@/lib/http';
import * as repo from '@/lib/repo';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
export const GET = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(o => repo.listRevisions(o, id)); };
export const POST = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => repo.createRevision(o, id, (await body(req)).note), 201); };
