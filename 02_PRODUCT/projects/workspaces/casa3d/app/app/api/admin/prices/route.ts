import { readJsonLimited } from '@/lib/body-limit';
import { admin } from '@/lib/adminRoute';
import { updatePrice, priceHistory } from '@/lib/outbound';
export const dynamic = 'force-dynamic';
export const POST = (req: Request) => admin(req, async () => updatePrice(await readJsonLimited(req, 16 * 1024)));
export const GET = (req: Request) => admin(req, async () => { const u = new URL(req.url); return priceHistory(u.searchParams.get('kind') || '', u.searchParams.get('id') || ''); });
