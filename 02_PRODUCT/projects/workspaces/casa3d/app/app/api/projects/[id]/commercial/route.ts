import { handle } from '@/lib/http';
import { getCommercial, saveCommercial } from '@/lib/commercial';
import { readJsonLimited } from '@/lib/body-limit';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
// Doar proprietarul: discount/adaos pe furnizor, raportul de profit, graficul de plăți, setările proformei.
export const GET = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(o => getCommercial(o, id)); };
// body: CommercialData (core/commercial.ts), validat strict pe server
export const PUT = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => saveCommercial(o, id, await readJsonLimited(req, 65536))); };
