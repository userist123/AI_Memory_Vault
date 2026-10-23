import { handle } from '@/lib/http';
import { getCatalog } from '@/lib/repo';
export const dynamic = 'force-dynamic';
export const GET = () => handle(() => getCatalog());
