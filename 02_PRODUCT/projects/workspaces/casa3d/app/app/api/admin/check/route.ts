import { admin } from '@/lib/adminRoute';
import { checkLinks } from '@/lib/outbound';
export const dynamic = 'force-dynamic';
export const maxDuration = 60;
export const POST = (req: Request) => admin(req, () => checkLinks());
