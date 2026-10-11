import { handle, body } from '@/lib/http';
import * as repo from '@/lib/repo';
import { moveToTrash } from '@/lib/trash';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
export const GET = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(o => repo.getProject(o, id)); };
export const PUT = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => { const b = await body(req); return repo.saveDraft(o, id, b.snapshot, b.base); }); };
// ștergerea mută proiectul în coș (30 de zile); ștergerea definitivă e /purge
export const DELETE = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(o => moveToTrash(o, id)); };
