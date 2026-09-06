import { NextRequest, NextResponse } from 'next/server';
import { z } from 'zod';
import { getCurrentUser } from '@/lib/auth/session';
import { runBacktest } from '@/lib/backtest/engine';
import { fetchBinanceCandles } from '@/lib/market/binance-data';
import { DEFAULT_SIGNAL_CONFIG } from '@/lib/signals/detector';
import { consumeQuota, quotaExceededResponse } from '@/lib/billing/quota';

export const runtime = 'nodejs';
export const maxDuration = 90;

const BacktestRequestSchema = z.object({
  symbol: z.string().min(1),
  timeframe: z.enum(['15m', '1h', '4h', '1d']),
  bars: z.number().min(100).max(1000).default(500),
  initialCapital: z.number().min(100).default(10000),
  riskPerTradePct: z.number().min(0.1).max(10).default(1),
  commissionPct: z.number().min(0).max(1).default(0.001), // 0.1% default
  maxOpenPositions: z.number().min(1).max(10).default(1),
  config: z.object({
    minStrength: z.number().min(0).max(100).optional(),
    minRiskReward: z.number().min(0).max(10).optional(),
    rsiOversold: z.number().min(0).max(50).optional(),
    rsiOverbought: z.number().min(50).max(100).optional(),
    enabledSignals: z.array(z.string()).optional(),
  }).optional(),
});

export async function POST(req: NextRequest) {
  try {
    const user = await getCurrentUser();
    if (!user) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });

    // Backtester uses tradeReview quota (we reuse existing feature key)
    const quota = await consumeQuota(user._id!, 'tradeReview');
    if (!quota.allowed) {
      const resp = quotaExceededResponse(quota);
      return NextResponse.json(resp.body, { status: resp.status });
    }

    const body = await req.json();
    const parsed = BacktestRequestSchema.safeParse(body);
    if (!parsed.success) {
      return NextResponse.json({ error: 'Invalid', details: parsed.error.errors }, { status: 400 });
    }

    const params = parsed.data;

    // Fetch candles
    const candles = await fetchBinanceCandles(params.symbol, params.timeframe, params.bars);

    // Build signal config
    const signalConfig = {
      ...DEFAULT_SIGNAL_CONFIG,
      ...(params.config || {}),
      enabledSignals: (params.config?.enabledSignals as typeof DEFAULT_SIGNAL_CONFIG.enabledSignals)
        || DEFAULT_SIGNAL_CONFIG.enabledSignals,
    };

    const result = runBacktest({
      symbol: params.symbol,
      timeframe: params.timeframe,
      candles,
      initialCapital: params.initialCapital,
      riskPerTradePct: params.riskPerTradePct,
      commissionPct: params.commissionPct,
      signalConfig,
      maxOpenPositions: params.maxOpenPositions,
      lookbackBars: 100,
    });

    // Trim equity curve for response size (keep only 500 points)
    if (result.equityCurve.length > 500) {
      const step = Math.ceil(result.equityCurve.length / 500);
      result.equityCurve = result.equityCurve.filter((_, i) => i % step === 0);
    }

    return NextResponse.json({ result });
  } catch (err: unknown) {
    const e = err as { message?: string };
    console.error('[Backtest] Error:', e);
    return NextResponse.json({ error: 'Backtest failed', details: e.message }, { status: 500 });
  }
}
