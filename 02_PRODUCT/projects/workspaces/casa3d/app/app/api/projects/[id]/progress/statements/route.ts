import { handle } from '@/lib/http';
import { saveStatement } from '@/lib/progress';
import { readJsonLimited } from '@/lib/body-limit';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
// POST body: StatementInput (core/progress.ts); id și number lipsă = le dă serverul (următorul număr)
export const POST = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => saveStatement(o, id, await readJsonLimited(req, 512 * 1024)), 201); };
