import { handle, body } from '@/lib/http';
import * as design from '@/lib/design';
export const dynamic = 'force-dynamic';
export const maxDuration = 60;
type Ctx = { params: Promise<{ id: string }> };
export const GET = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(o => design.listDesigns(o, id)); };
// body: { brief: { roomId, wants[], budget?, retailers?, accessibility?, replace? } }
export const POST = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => design.generateDesign(o, id, (await body(req)).brief), 201); };
