import { handle } from '@/lib/http';
import { listChoices } from '@/lib/room-choices';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
// Doar proprietarul: ce variantă a ales clientul în fiecare cameră.
export const GET = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(o => listChoices(o, id)); };
