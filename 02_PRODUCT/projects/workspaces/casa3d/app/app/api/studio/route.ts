import { handle, body } from '@/lib/http';
import { getProfile, saveProfile } from '@/lib/studio-profile';
export const dynamic = 'force-dynamic';
export const GET = () => handle(o => getProfile(o));
export const PUT = async (req: Request) => handle(async o => saveProfile(o, await body(req)));
