import { handle, body } from '@/lib/http';
import * as repo from '@/lib/repo';
export const dynamic = 'force-dynamic';
export const GET = () => handle(o => repo.listProjects(o));
export const POST = async (req: Request) => handle(async o => { const b = await body(req); return { id: await repo.createProject(o, b.name, b.template ?? 'demo') }; }, 201);
