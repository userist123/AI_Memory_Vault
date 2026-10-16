import { handle, body } from '@/lib/http';
import * as repo from '@/lib/repo';
import { projectLang } from '@/core/templates';
// limba proiectului nou: cea a interfeței (trimisă de client), altfel Accept-Language, altfel româna (core/templates.ts)
export const dynamic = 'force-dynamic';
export const GET = () => handle(o => repo.listProjects(o));
export const POST = async (req: Request) => handle(async o => { const b = await body(req); return { id: await repo.createProject(o, b.name, b.template ?? 'demo', projectLang(b.lang, req.headers.get('accept-language'))) }; }, 201);
