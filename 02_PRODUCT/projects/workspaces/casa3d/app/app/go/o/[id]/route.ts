import { goHandler } from '@/lib/goRoute';
export const dynamic = 'force-dynamic';
export const GET = async (req: Request, { params }: { params: Promise<{ id: string }> }) => goHandler(req, 'o', (await params).id);
