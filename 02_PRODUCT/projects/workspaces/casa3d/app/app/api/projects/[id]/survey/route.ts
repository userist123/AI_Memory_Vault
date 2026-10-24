import { handle } from '@/lib/http';
import { listSurveys, saveSurvey, deleteSurvey } from '@/lib/surveys';
import { readJsonLimited } from '@/lib/body-limit';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
export const GET = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(o => listSurveys(o, id)); };
// body: { roomId, n, e, s, w, d1?, d2?, note? } (cm)
export const PUT = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => { const b = await readJsonLimited(req, 4096) as any; return saveSurvey(o, id, b?.roomId, b); }); };
export const DELETE = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(o => deleteSurvey(o, id, new URL(req.url).searchParams.get('room'))); };
