import { handle, body } from '@/lib/http';
import * as repo from '@/lib/repo';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
// body: ProjectMeta (core/project-meta.ts) — fișa proiectului, doar pentru proprietar
export const PUT = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => repo.saveMeta(o, id, await body(req))); };
