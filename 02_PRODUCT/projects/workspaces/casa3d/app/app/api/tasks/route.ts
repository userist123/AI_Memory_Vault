import { handle } from '@/lib/http';
import { listTasks, addTask } from '@/lib/tasks';
import { readJsonLimited } from '@/lib/body-limit';
export const dynamic = 'force-dynamic';
// ?project=<id> pentru sarcinile unui proiect
export const GET = async (req: Request) => { const p = new URL(req.url).searchParams.get('project'); return handle(o => listTasks(o, p)); };
// body: { title, due?, projectId? }
export const POST = async (req: Request) => handle(async o => addTask(o, await readJsonLimited(req, 4096)), 201);
