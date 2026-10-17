import { handle } from '@/lib/http';
import { restoreFromTrash } from '@/lib/trash';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
export const POST = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(o => restoreFromTrash(o, id)); };
