import { admin } from '@/lib/adminRoute';
import { addLink } from '@/lib/outbound';
export const dynamic = 'force-dynamic';
export const POST = (req: Request) => admin(req, async () => addLink(await req.json()));
