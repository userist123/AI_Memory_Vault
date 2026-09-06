import { NextRequest, NextResponse } from 'next/server';
import { z } from 'zod';
import { getCurrentUser } from '@/lib/auth/session';
import { scanSymbols, dispatchSignalsToUsers } from '@/lib/signals/scanner';
import { consumeQuota, quotaExceededResponse } from '@/lib/billing/quota';
import type { Timeframe } from '@/lib/market/binance-data';

export const runtime = 'nodejs';
export const maxDuration = 60;

const ScanRequestSchema = z.object({
  symbols: z.array(z.string()).min(1).max(20).optional(),
  timeframes: z.array(z.enum(['5m', '15m', '1h', '4h', '1d'])).min(1).max(3).optional(),
});

export async function POST(req: NextRequest) {
  try {
    const user = await getCurrentUser();
    if (!user) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });

    // Scanner uses marketScanner quota (Elite-only feature)
    const quota = await consumeQuota(user._id!, 'marketScanner');
    if (!quota.allowed) {
      const resp = quotaExceededResponse(quota);
      return NextResponse.json(resp.body, { status: resp.status });
    }

    const body = await req.json().catch(() => ({}));
    const parsed = ScanRequestSchema.safeParse(body);
    if (!parsed.success) {
      return NextResponse.json(
        { error: 'Invalid request', details: parsed.error.errors },
        { status: 400 }
      );
    }

    // Defaults: top 10 Binance USDT pairs, 1h + 4h timeframes
    const symbols = parsed.data.symbols || [
      'BTCUSDT', 'ETHUSDT', 'SOLUSDT', 'BNBUSDT', 'XRPUSDT',
      'ADAUSDT', 'DOGEUSDT', 'AVAXUSDT', 'LINKUSDT', 'DOTUSDT',
    ];
    const timeframes: Timeframe[] = parsed.data.timeframes as Timeframe[] || ['1h', '4h'];

    const signals = await scanSymbols(symbols, timeframes);

    // Dispatch to this user (saves alerts + sends notifications)
    const dispatch = await dispatchSignalsToUsers(signals, [user._id!]);

    return NextResponse.json({
      signals: signals.slice(0, 50), // cap response
      totalFound: signals.length,
      ...dispatch,
    });
  } catch (err: unknown) {
    const e = err as { message?: string };
    console.error('[Scan] Error:', e);
    return NextResponse.json({ error: 'Scan failed', details: e.message }, { status: 500 });
  }
}
