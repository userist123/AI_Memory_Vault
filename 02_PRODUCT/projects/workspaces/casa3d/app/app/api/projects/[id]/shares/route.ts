import { handle, body } from '@/lib/http';
import * as share from '@/lib/share';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
export const GET = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(o => share.listShares(o, id)); };
// body: { revision }
export const POST = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => share.createShare(o, id, (await body(req)).revision), 201); };
// body: { token }
export const DELETE = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => share.revokeShare(o, id, (await body(req)).token)); };
