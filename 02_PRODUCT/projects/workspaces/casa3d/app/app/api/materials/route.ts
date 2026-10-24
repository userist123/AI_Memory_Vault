import { handle } from '@/lib/http';
import { getMaterials } from '@/lib/repo';
export const dynamic = 'force-dynamic';
export const GET = () => handle(() => getMaterials());
