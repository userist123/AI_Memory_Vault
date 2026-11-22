import { handle } from '@/lib/http';
import { saveStatement, deleteStatement } from '@/lib/progress';
import { readJsonLimited } from '@/lib/body-limit';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string; sid: string }> };
// PUT body: StatementInput (numărul rămâne cel al situației); DELETE: doar ultima situație
export const PUT = async (req: Request, { params }: Ctx) => { const { id, sid } = await params; return handle(async o => saveStatement(o, id, await readJsonLimited(req, 512 * 1024), sid)); };
export const DELETE = async (_: Request, { params }: Ctx) => { const { id, sid } = await params; return handle(o => deleteStatement(o, id, sid)); };
