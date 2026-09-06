import { NextRequest, NextResponse } from 'next/server';
import { getCurrentUser } from '@/lib/auth/session';
import { getUserAlerts } from '@/lib/db/alerts';

export const runtime = 'nodejs';

export async function GET(req: NextRequest) {
  const user = await getCurrentUser();
  if (!user) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });

  const { searchParams } = new URL(req.url);
  const status = searchParams.get('status') as 'pending' | 'executed' | 'skipped' | 'expired' | null;
  const limit = Math.min(parseInt(searchParams.get('limit') || '50'), 200);

  const alerts = await getUserAlerts(user._id!, {
    status: status || undefined,
    limit,
  });

  return NextResponse.json({ alerts });
}
