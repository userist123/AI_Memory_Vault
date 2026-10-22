import { handle } from '@/lib/http';
import { getContractBundle, saveContract } from '@/lib/contract';
import { readJsonLimited } from '@/lib/body-limit';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
// GET: datele contractului + calculul onorariului + textul contractului și al ofertei (doar proprietarul)
export const GET = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(o => getContractBundle(o, id)); };
// PUT body: ContractData (core/contract.ts)
export const PUT = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => { await saveContract(o, id, await readJsonLimited(req, 16 * 1024)); return getContractBundle(o, id); }); };
