import { handle } from '@/lib/http';
import { HttpError } from '@/lib/repo';
import { getProgress, getProgressDoc, fixBaseline } from '@/lib/progress';
import { readJsonLimited } from '@/lib/body-limit';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
// GET: devizul de bază, situațiile și avertismentele; ?doc=1 adaugă executantul (profilul studioului), pentru planșa tipăribilă
export const GET = async (req: Request, { params }: Ctx) => { const { id } = await params, doc = new URL(req.url).searchParams.get('doc') === '1';
  return handle(o => doc ? getProgressDoc(o, id) : getProgress(o, id)); };
// POST body: { action: 'fixBaseline' } — îngheață oferta curentă ca deviz de bază (409 dacă există deja situații)
export const POST = async (req: Request, { params }: Ctx) => { const { id } = await params;
  return handle(async o => { const b = await readJsonLimited(req, 1024) as any; if (b?.action !== 'fixBaseline') throw new HttpError(400, 'Acțiune necunoscută.'); return fixBaseline(o, id); }); };
