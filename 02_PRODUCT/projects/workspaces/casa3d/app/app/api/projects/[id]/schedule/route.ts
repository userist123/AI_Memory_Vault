import { handle } from '@/lib/http';
import { getSchedule, saveSchedule } from '@/lib/schedule';
import { readJsonLimited } from '@/lib/body-limit';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
export const GET = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(o => getSchedule(o, id)); };
// PUT body: { phases: Phase[] } (core/schedule.ts)
export const PUT = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => saveSchedule(o, id, await readJsonLimited(req, 32 * 1024))); };
