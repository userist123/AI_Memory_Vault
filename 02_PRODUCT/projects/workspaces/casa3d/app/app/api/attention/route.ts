import { handle } from '@/lib/http';
import { getAttention } from '@/lib/attention';
export const dynamic = 'force-dynamic';
// „De urmărit azi”: ce cere atenție pe proiectele active ale proprietarului. ?fresh=1 recalculează, fără rezultatul ținut în memorie
export const GET = async (req: Request) => { const fresh = new URL(req.url).searchParams.get('fresh') === '1'; return handle(o => getAttention(o, { fresh })); };
