import { handle } from '@/lib/http';
import { issueProforma } from '@/lib/commercial';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
// Emiterea proformei (doar proprietarul): îngheață documentul curent sub seria și numărul lui; 409 dacă numărul e deja emis.
export const POST = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(o => issueProforma(o, id), 201); };
