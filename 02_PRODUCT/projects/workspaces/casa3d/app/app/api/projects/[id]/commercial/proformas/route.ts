import { handle } from '@/lib/http';
import { listIssuedProformas } from '@/lib/commercial';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
// Proformele emise ale proiectului (serie, număr, data, total), cele mai noi întâi; doar proprietarul.
export const GET = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(o => listIssuedProformas(o, id)); };
