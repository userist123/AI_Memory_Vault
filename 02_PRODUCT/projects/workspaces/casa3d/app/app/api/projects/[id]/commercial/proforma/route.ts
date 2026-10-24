import { handle } from '@/lib/http';
import { getProforma } from '@/lib/commercial';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
// Proforma (doar prețuri de client) pentru pagina tipăribilă /p/[id]/proforma; doar proprietarul.
export const GET = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(o => getProforma(o, id)); };
