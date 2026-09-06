import type { Candle } from '@/lib/signals/indicators';
import type { Signal, SignalConfig } from '@/lib/signals/detector';
import { detectSignals, DEFAULT_SIGNAL_CONFIG } from '@/lib/signals/detector';

export interface BacktestTrade {
  entryTime: number;
  entryPrice: number;
  exitTime: number;
  exitPrice: number;
  direction: 'long' | 'short';
  quantity: number;
  pnl: number;
  pnlPct: number;
  rMultiple: number;
  exitReason: 'take_profit' | 'stop_loss' | 'end_of_data' | 'time_exit';
  signalType: string;
  signalReason: string;
}

export interface BacktestParams {
  symbol: string;
  timeframe: string;
  candles: Candle[];
  // Initial capital in quote currency (USDT, USD)
  initialCapital: number;
  // Risk per trade as % of account
  riskPerTradePct: number;
  // Commission per trade as % (Binance spot = 0.1%)
  commissionPct: number;
  // Strategy config
  signalConfig: SignalConfig;
  // Max parallel positions
  maxOpenPositions: number;
  // Lookback window for signal detection (how much history to pass each bar)
  lookbackBars: number;
}

export interface BacktestResult {
  // Inputs (for display)
  symbol: string;
  timeframe: string;
  startDate: Date;
  endDate: Date;
  totalBars: number;
  initialCapital: number;
  finalCapital: number;

  // Core metrics
  totalTrades: number;
  wins: number;
  losses: number;
  winRate: number; // %

  // Profitability
  totalPnL: number;
  totalPnLPct: number;
  profitFactor: number; // gross win / gross loss
  avgWin: number;
  avgLoss: number;
  avgRMultiple: number;
  bestTrade: number;
  worstTrade: number;

  // Risk
  maxDrawdown: number;
  maxDrawdownPct: number;
  sharpeRatio: number;

  // Streaks
  maxConsecutiveWins: number;
  maxConsecutiveLosses: number;

  // Equity curve (for charting)
  equityCurve: Array<{ time: number; equity: number }>;

  // All trades
  trades: BacktestTrade[];

  // Warnings
  warnings: string[];
}

/**
 * Run a backtest on historical candles using the signal detector.
 *
 * Bar-by-bar simulation:
 * 1. For each candle, run signal detection on candles[0..i]
 * 2. If signal → enter position with risk-based sizing
 * 3. Track position until SL or TP hit
 * 4. Record trade, update equity
 */
export function runBacktest(params: BacktestParams): BacktestResult {
  const {
    symbol,
    timeframe,
    candles,
    initialCapital,
    riskPerTradePct,
    commissionPct,
    signalConfig,
    maxOpenPositions,
    lookbackBars,
  } = params;

  if (candles.length < lookbackBars + 10) {
    return emptyResult(symbol, timeframe, candles, initialCapital, [
      `Not enough bars: need ${lookbackBars + 10}, got ${candles.length}`,
    ]);
  }

  const trades: BacktestTrade[] = [];
  const equityCurve: Array<{ time: number; equity: number }> = [];
  const warnings: string[] = [];

  interface OpenPosition {
    entryBar: number;
    entryTime: number;
    entryPrice: number;
    direction: 'long' | 'short';
    quantity: number;
    stopLoss: number;
    takeProfit: number;
    signalType: string;
    signalReason: string;
    initialRisk: number; // price diff for R-multiple calc
  }

  let capital = initialCapital;
  let peakCapital = capital;
  let maxDD = 0;
  let maxDDPct = 0;
  const openPositions: OpenPosition[] = [];

  // Iterate bar-by-bar, starting when we have enough history
  for (let i = lookbackBars; i < candles.length; i++) {
    const currentCandle = candles[i];
    const prevCandle = candles[i - 1];

    // 1. Check open positions for SL/TP hits (use current bar's high/low)
    for (let p = openPositions.length - 1; p >= 0; p--) {
      const pos = openPositions[p];
      let exitPrice: number | null = null;
      let exitReason: BacktestTrade['exitReason'] | null = null;

      if (pos.direction === 'long') {
        // Check SL first (conservative - assume worst case if both hit)
        if (currentCandle.low <= pos.stopLoss) {
          exitPrice = pos.stopLoss;
          exitReason = 'stop_loss';
        } else if (currentCandle.high >= pos.takeProfit) {
          exitPrice = pos.takeProfit;
          exitReason = 'take_profit';
        }
      } else {
        if (currentCandle.high >= pos.stopLoss) {
          exitPrice = pos.stopLoss;
          exitReason = 'stop_loss';
        } else if (currentCandle.low <= pos.takeProfit) {
          exitPrice = pos.takeProfit;
          exitReason = 'take_profit';
        }
      }

      if (exitPrice !== null && exitReason !== null) {
        const grossPnl =
          pos.direction === 'long'
            ? (exitPrice - pos.entryPrice) * pos.quantity
            : (pos.entryPrice - exitPrice) * pos.quantity;

        // Commission on both sides
        const commission =
          (pos.entryPrice * pos.quantity + exitPrice * pos.quantity) * commissionPct;
        const netPnl = grossPnl - commission;

        const rMultiple = pos.initialRisk > 0 ? netPnl / (pos.initialRisk * pos.quantity) : 0;
        const pnlPct = (netPnl / (pos.entryPrice * pos.quantity)) * 100;

        trades.push({
          entryTime: pos.entryTime,
          entryPrice: pos.entryPrice,
          exitTime: currentCandle.time,
          exitPrice,
          direction: pos.direction,
          quantity: pos.quantity,
          pnl: netPnl,
          pnlPct,
          rMultiple,
          exitReason,
          signalType: pos.signalType,
          signalReason: pos.signalReason,
        });

        capital += netPnl;
        openPositions.splice(p, 1);
      }
    }

    // 2. Look for new signals (only if we have room)
    if (openPositions.length < maxOpenPositions) {
      const historicalCandles = candles.slice(Math.max(0, i - lookbackBars), i + 1);
      const signals = detectSignals(symbol, timeframe, historicalCandles, signalConfig);

      // Take strongest signal (if any)
      if (signals.length > 0) {
        const signal = signals.sort((a, b) => b.strength - a.strength)[0];

        // Risk-based position sizing
        const riskAmount = capital * (riskPerTradePct / 100);
        const priceDiff = Math.abs(signal.entry - signal.stopLoss);
        const quantity = priceDiff > 0 ? riskAmount / priceDiff : 0;

        if (quantity > 0 && signal.entry * quantity <= capital * 0.95) {
          // Enter at next bar's open (realistic - we can't fill at close)
          if (i + 1 < candles.length) {
            const nextBar = candles[i + 1];
            openPositions.push({
              entryBar: i + 1,
              entryTime: nextBar.time,
              entryPrice: nextBar.open,
              direction: signal.direction,
              quantity,
              stopLoss: signal.stopLoss,
              takeProfit: signal.takeProfit,
              signalType: signal.type,
              signalReason: signal.reason,
              initialRisk: priceDiff,
            });
          }
        }
      }
    }

    // 3. Track equity (mark-to-market)
    let unrealizedPnl = 0;
    for (const pos of openPositions) {
      unrealizedPnl +=
        pos.direction === 'long'
          ? (currentCandle.close - pos.entryPrice) * pos.quantity
          : (pos.entryPrice - currentCandle.close) * pos.quantity;
    }
    const equity = capital + unrealizedPnl;

    if (equity > peakCapital) peakCapital = equity;
    const dd = peakCapital - equity;
    if (dd > maxDD) {
      maxDD = dd;
      maxDDPct = (dd / peakCapital) * 100;
    }

    equityCurve.push({ time: currentCandle.time, equity });
  }

  // 4. Close any remaining open positions at final bar
  const finalCandle = candles[candles.length - 1];
  for (const pos of openPositions) {
    const grossPnl =
      pos.direction === 'long'
        ? (finalCandle.close - pos.entryPrice) * pos.quantity
        : (pos.entryPrice - finalCandle.close) * pos.quantity;
    const commission = (pos.entryPrice * pos.quantity + finalCandle.close * pos.quantity) * commissionPct;
    const netPnl = grossPnl - commission;

    trades.push({
      entryTime: pos.entryTime,
      entryPrice: pos.entryPrice,
      exitTime: finalCandle.time,
      exitPrice: finalCandle.close,
      direction: pos.direction,
      quantity: pos.quantity,
      pnl: netPnl,
      pnlPct: (netPnl / (pos.entryPrice * pos.quantity)) * 100,
      rMultiple: pos.initialRisk > 0 ? netPnl / (pos.initialRisk * pos.quantity) : 0,
      exitReason: 'end_of_data',
      signalType: pos.signalType,
      signalReason: pos.signalReason,
    });
    capital += netPnl;
  }

  // ===== Compute final metrics =====
  const wins = trades.filter((t) => t.pnl > 0);
  const losses = trades.filter((t) => t.pnl < 0);
  const grossWin = wins.reduce((s, t) => s + t.pnl, 0);
  const grossLoss = Math.abs(losses.reduce((s, t) => s + t.pnl, 0));

  // Consecutive wins/losses
  let maxConsecWins = 0;
  let maxConsecLosses = 0;
  let currConsecWins = 0;
  let currConsecLosses = 0;
  for (const t of trades) {
    if (t.pnl > 0) {
      currConsecWins++;
      currConsecLosses = 0;
      if (currConsecWins > maxConsecWins) maxConsecWins = currConsecWins;
    } else if (t.pnl < 0) {
      currConsecLosses++;
      currConsecWins = 0;
      if (currConsecLosses > maxConsecLosses) maxConsecLosses = currConsecLosses;
    }
  }

  // Sharpe ratio (simplified: avg return / std of returns, annualized)
  const returns = trades.map((t) => t.pnlPct);
  const avgReturn = returns.length > 0 ? returns.reduce((a, b) => a + b, 0) / returns.length : 0;
  const variance =
    returns.length > 0
      ? returns.reduce((s, r) => s + (r - avgReturn) ** 2, 0) / returns.length
      : 0;
  const stdDev = Math.sqrt(variance);
  // Rough annualization: assumes ~1 trade per day on average
  const sharpe = stdDev > 0 ? (avgReturn / stdDev) * Math.sqrt(252) : 0;

  const pnls = trades.map((t) => t.pnl);
  const bestTrade = pnls.length > 0 ? Math.max(...pnls) : 0;
  const worstTrade = pnls.length > 0 ? Math.min(...pnls) : 0;

  const rMultiples = trades.map((t) => t.rMultiple).filter((r) => isFinite(r));
  const avgR = rMultiples.length > 0 ? rMultiples.reduce((a, b) => a + b, 0) / rMultiples.length : 0;

  // Warnings
  if (trades.length < 20) {
    warnings.push(`Doar ${trades.length} trades generate. Rezultate nu sunt statistic semnificative. Încearcă perioadă mai lungă sau relaxează criteriile.`);
  }
  if (trades.length > 0 && wins.length === 0) {
    warnings.push('Zero trades câștigătoare. Strategia nu funcționează pe această piață/timeframe.');
  }
  const durationDays = (finalCandle.time - candles[lookbackBars].time) / (1000 * 60 * 60 * 24);
  if (durationDays < 180) {
    warnings.push(`Perioadă de test scurtă (${Math.round(durationDays)} zile). Pentru validare reală, testează pe 2-5 ani cu regime bull + bear + crab.`);
  }

  return {
    symbol,
    timeframe,
    startDate: new Date(candles[lookbackBars].time),
    endDate: new Date(finalCandle.time),
    totalBars: candles.length,
    initialCapital,
    finalCapital: capital,
    totalTrades: trades.length,
    wins: wins.length,
    losses: losses.length,
    winRate: trades.length > 0 ? (wins.length / trades.length) * 100 : 0,
    totalPnL: capital - initialCapital,
    totalPnLPct: ((capital - initialCapital) / initialCapital) * 100,
    profitFactor: grossLoss > 0 ? grossWin / grossLoss : wins.length > 0 ? 999 : 0,
    avgWin: wins.length > 0 ? grossWin / wins.length : 0,
    avgLoss: losses.length > 0 ? -grossLoss / losses.length : 0,
    avgRMultiple: avgR,
    bestTrade,
    worstTrade,
    maxDrawdown: maxDD,
    maxDrawdownPct: maxDDPct,
    sharpeRatio: sharpe,
    maxConsecutiveWins: maxConsecWins,
    maxConsecutiveLosses: maxConsecLosses,
    equityCurve,
    trades,
    warnings,
  };
}

function emptyResult(
  symbol: string,
  timeframe: string,
  candles: Candle[],
  initialCapital: number,
  warnings: string[]
): BacktestResult {
  return {
    symbol,
    timeframe,
    startDate: candles[0] ? new Date(candles[0].time) : new Date(),
    endDate: candles.length > 0 ? new Date(candles[candles.length - 1].time) : new Date(),
    totalBars: candles.length,
    initialCapital,
    finalCapital: initialCapital,
    totalTrades: 0,
    wins: 0,
    losses: 0,
    winRate: 0,
    totalPnL: 0,
    totalPnLPct: 0,
    profitFactor: 0,
    avgWin: 0,
    avgLoss: 0,
    avgRMultiple: 0,
    bestTrade: 0,
    worstTrade: 0,
    maxDrawdown: 0,
    maxDrawdownPct: 0,
    sharpeRatio: 0,
    maxConsecutiveWins: 0,
    maxConsecutiveLosses: 0,
    equityCurve: [],
    trades: [],
    warnings,
  };
}
