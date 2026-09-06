import { NextRequest, NextResponse } from 'next/server';
import { getTradesByUser, getTradeStats } from '@/lib/db/mongo';
import { getUserIdFromRequest } from '@/lib/auth/session';

export const runtime = 'nodejs';

export async function GET(req: NextRequest) {
  try {
    const userId = await getUserIdFromRequest(req);
    if (!userId) {
      return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
    }

    const { searchParams } = new URL(req.url);
    const limit = Math.min(parseInt(searchParams.get('limit') || '100'), 1000);
    const offset = parseInt(searchParams.get('offset') || '0');
    const symbol = searchParams.get('symbol') || undefined;
    const broker = searchParams.get('broker') || undefined;
    const status = searchParams.get('status') || undefined;
    const withStats = searchParams.get('stats') === 'true';

    const trades = await getTradesByUser(userId, {
      limit,
      offset,
      symbol,
      broker,
      status,
    });

    const response: { trades: typeof trades; stats?: unknown } = { trades };

    if (withStats) {
      response.stats = await getTradeStats(userId);
    }

    return NextResponse.json(response);
  } catch (err: unknown) {
    const e = err as { message?: string };
    console.error('[Trades] List error:', e);
    return NextResponse.json(
      { error: 'Failed to list trades', details: e.message },
      { status: 500 }
    );
  }
}
