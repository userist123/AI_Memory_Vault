import { handle } from '@/lib/http';
import { outboundStatus } from '@/lib/outbound';
export const dynamic = 'force-dynamic';
export const GET = () => handle(() => outboundStatus());
