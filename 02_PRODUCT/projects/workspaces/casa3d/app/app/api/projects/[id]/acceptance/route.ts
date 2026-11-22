import { handle, body } from '@/lib/http';
import { acceptanceAdmin, setOfferAccept } from '@/lib/offer-acceptance';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
// Doar proprietarul. GET: linkurile active (fiecare cu comutatorul lui), starea ofertei față de acceptări, ce s-a schimbat după
// acceptare și istoricul acceptărilor.
export const GET = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(o => acceptanceAdmin(o, id)); };
// PUT body: { token, enabled: boolean } — „Acceptare online” pe linkul numit (cere PIN pe acel link: 409 fără el).
export const PUT = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => { const b = await body(req, 1024); return setOfferAccept(o, id, b?.token, b?.enabled); }); };
