import { handle } from '@/lib/http';
import { updateTask, deleteTask } from '@/lib/tasks';
import { readJsonLimited } from '@/lib/body-limit';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ task: string }> };
export const PATCH = async (req: Request, { params }: Ctx) => { const { task } = await params; return handle(async o => updateTask(o, task, await readJsonLimited(req, 4096))); };
export const DELETE = async (_: Request, { params }: Ctx) => { const { task } = await params; return handle(o => deleteTask(o, task)); };
