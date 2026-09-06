import { NextRequest, NextResponse } from 'next/server';
import { scanSymbols, dispatchSignalsToUsers } from '@/lib/signals/scanner';
import { getDb } from '@/lib/db/mongo';
import { getTopBinancePairs, type Timeframe } from '@/lib/market/binance-data';
import { isFeatureEnabled } from '@/lib/billing/plans';

export const runtime = 'nodejs';
export const maxDuration = 120;

/**
 * Cron endpoint - runs every 15 min via Cloudflare Cron Triggers or Vercel Cron.
 *
 * Authentication: checks CRON_SECRET header to prevent public abuse.
 * For Cloudflare Workers Cron Triggers, the trigger is internal (no HTTP).
 * For Vercel Cron or manual trigger, provide CRON_SECRET in header.
 */
export async function GET(req: NextRequest) {
  try {
    // Auth check
    const authHeader = req.headers.get('authorization');
    const secret = process.env.CRON_SECRET;
    if (secret && authHeader !== `Bearer ${secret}`) {
      return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
    }

    console.log('[Cron] Starting scheduled scan...');
    const started = Date.now();

    // 1. Find all users with alerts enabled + active plans that support signals
    const db = await getDb();
    let activeUserIds: string[] = [];

    if (db) {
      const prefsCol = db.collection('notification_prefs');
      const prefs = await prefsCol
        .find({
          $or: [
            { 'email.enabled': true },
            { 'telegram.enabled': true, 'telegram.chatId': { $exists: true } },
            { 'inApp.enabled': true },
          ],
        })
        .toArray();

      // Filter users by plan (must have maxSignalsPerMonth > 0 or unlimited)
      const usersCol = db.collection('users');
      for (const p of prefs) {
        const user = await usersCol.findOne({ _id: p.userId } as unknown as Record<string, unknown>);
        if (!user) continue;
        const plan = (user as { plan?: string }).plan || 'free';
        // Free gets 10/month - we filter here, quota enforced in dispatch
        if (isFeatureEnabled(plan as 'free' | 'pro' | 'elite' | 'autopilot', 'maxSignalsPerMonth' as never) !== false) {
          activeUserIds.push(String(p.userId));
        }
      }
    }

    if (activeUserIds.length === 0) {
      return NextResponse.json({
        ok: true,
        message: 'No active users with alerts enabled',
        duration: Date.now() - started,
      });
    }

    // 2. Get top-20 Binance pairs
    let topPairs: string[];
    try {
      topPairs = await getTopBinancePairs(20, 'USDT');
    } catch (err) {
      console.warn('[Cron] Failed to fetch top pairs, using defaults');
      topPairs = [
        'BTCUSDT', 'ETHUSDT', 'SOLUSDT', 'BNBUSDT', 'XRPUSDT',
        'ADAUSDT', 'DOGEUSDT', 'AVAXUSDT', 'LINKUSDT', 'DOTUSDT',
      ];
    }

    // 3. Scan on multiple timeframes
    const timeframes: Timeframe[] = ['1h', '4h'];
    const signals = await scanSymbols(topPairs, timeframes);

    console.log(`[Cron] Found ${signals.length} signals across ${topPairs.length} pairs`);

    // 4. Dispatch to users (filters applied per-user)
    const dispatch = await dispatchSignalsToUsers(signals, activeUserIds);

    const duration = Date.now() - started;
    console.log(`[Cron] Completed in ${duration}ms:`, dispatch);

    return NextResponse.json({
      ok: true,
      signalsFound: signals.length,
      activeUsers: activeUserIds.length,
      ...dispatch,
      duration,
    });
  } catch (err: unknown) {
    const e = err as { message?: string };
    console.error('[Cron] Scan error:', e);
    return NextResponse.json({ error: 'Scan failed', details: e.message }, { status: 500 });
  }
}

/**
 * Cloudflare Workers calls via scheduled() handler, not HTTP.
 * For CF, export this and wire it in the worker entrypoint.
 */
export async function POST(req: NextRequest) {
  return GET(req);
}
