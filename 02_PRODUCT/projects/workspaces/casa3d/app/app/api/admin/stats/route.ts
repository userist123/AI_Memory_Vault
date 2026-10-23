import { admin } from '@/lib/adminRoute';
import { clickStats } from '@/lib/outbound';
export const dynamic = 'force-dynamic';
export const GET = (req: Request) => admin(req, () => clickStats());
