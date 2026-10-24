import { handle } from '@/lib/http';
import { listTrash } from '@/lib/trash';
export const dynamic = 'force-dynamic';
export const GET = () => handle(o => listTrash(o));
