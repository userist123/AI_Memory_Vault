import { handle, body } from '@/lib/http';
import * as repo from '@/lib/repo';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
export const GET = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(o => repo.getProject(o, id)); };
export const PUT = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => repo.saveDraft(o, id, (await body(req)).snapshot)); };
export const DELETE = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => { await repo.deleteProject(o, id); return { ok: true }; }); };
