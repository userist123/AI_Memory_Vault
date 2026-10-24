import { handle } from '@/lib/http';
import { clientLink } from '@/lib/share';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
// Linkul pentru client: versiunea curentă a proiectului (salvată ca revizie dacă s-a schimbat) și un link activ spre ea.
export const POST = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(o => clientLink(o, id), 201); };
