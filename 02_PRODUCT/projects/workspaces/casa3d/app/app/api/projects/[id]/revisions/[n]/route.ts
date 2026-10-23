import { handle } from '@/lib/http';
import * as repo from '@/lib/repo';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string; n: string }> };
export const POST = async (_: Request, { params }: Ctx) => { const { id, n } = await params; return handle(o => repo.restoreRevision(o, id, Number(n))); };
