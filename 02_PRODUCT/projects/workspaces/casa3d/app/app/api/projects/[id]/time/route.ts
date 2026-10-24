import { handle } from '@/lib/http';
import { getTimesheet, addTime, deleteTime } from '@/lib/timesheet';
import { readJsonLimited } from '@/lib/body-limit';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
export const GET = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(o => getTimesheet(o, id)); };
// body: { date: 'AAAA-LL-ZZ', minutes, kind, note? }
export const POST = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => addTime(o, id, await readJsonLimited(req, 2048)), 201); };
export const DELETE = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(o => deleteTime(o, id, new URL(req.url).searchParams.get('entry'))); };
