import { handle } from '@/lib/http';
import { getProforma, getIssuedProforma } from '@/lib/commercial';
import { HttpError } from '@/lib/repo';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
// Proforma (doar prețuri de client) pentru pagina tipăribilă /p/[id]/proforma; doar proprietarul.
// cu ?s=<serie>&n=<număr>: proforma emisă, exact cum a fost la emitere; fără: ciorna, calculată din proiectul curent
export const GET = async (req: Request, { params }: Ctx) => { const { id } = await params, u = new URL(req.url), s = u.searchParams.get('s'), n = u.searchParams.get('n');
  return handle(async o => { if (s == null && n == null) return getProforma(o, id);
    if (!s || !/^[\w.-]{1,20}$/.test(s) || !n || !/^\d{1,9}$/.test(n)) throw new HttpError(400, 'Seria sau numărul proformei sunt invalide.');
    return getIssuedProforma(o, id, s, Number(n)); }); };
