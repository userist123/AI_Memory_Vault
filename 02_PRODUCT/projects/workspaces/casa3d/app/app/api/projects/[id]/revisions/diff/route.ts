import { handle } from '@/lib/http';
import * as repo from '@/lib/repo';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
const num = (v: string | null) => v !== null && /^\d{1,9}$/.test(v) ? Number(v) : NaN; // NaN => 400 în repo
export const GET = async (req: Request, { params }: Ctx) => { const { id } = await params; const u = new URL(req.url), t = u.searchParams.get('to');
  return handle(o => repo.diffRevisions(o, id, num(u.searchParams.get('from')), t === 'draft' ? 'draft' : num(t))); };
