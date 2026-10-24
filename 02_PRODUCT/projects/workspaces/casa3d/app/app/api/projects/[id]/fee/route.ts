import { handle } from '@/lib/http';
import { getContractBundle, setFee } from '@/lib/contract';
import { readJsonLimited } from '@/lib/body-limit';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
// PUT body: { fee: { kind, amount, hours? } | null } — schimbă DOAR onorariul contractului (sursa unică), de ex. din buget
export const PUT = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => {
  const b = await readJsonLimited(req, 2048) as any; await setFee(o, id, b?.fee ?? null); const x = await getContractBundle(o, id); return { fee: x.effectiveFee, calc: x.calc }; }); };
