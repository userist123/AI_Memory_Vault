import { handle } from '@/lib/http';
import { getProject } from '@/lib/repo';
import { bnrRates } from '@/lib/fx-bnr';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
// Cursul BNR al zilei: { date, rates: { EUR: 5.3482, … }, fetchedAt }. Doar proprietarul proiectului (404 pentru altcineva);
// nu se salvează nimic — interfața pune cursul ales în proiect (Snapshot.fx), ca orice altă modificare.
export const GET = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => { await getProject(o, id); return bnrRates(); }); };
