import { handle } from '@/lib/http';
import { getHandover } from '@/lib/handover';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
export const GET = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(o => getHandover(o, id)); };
