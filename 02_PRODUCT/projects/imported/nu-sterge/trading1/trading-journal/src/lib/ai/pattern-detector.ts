import type { Trade } from '@/types/trade';

export interface TradePattern {
  type: string;
  severity: 1 | 2 | 3 | 4 | 5;
  count: number;
  description: string;
  evidence: string;
  affectedTradeIds: string[];
}

export interface PatternAnalysis {
  patterns: TradePattern[];
  metrics: {
    totalTrades: number;
    closedTrades: number;
    winRate: number;
    totalPnL: number;
    profitFactor: number;
    avgWin: number;
    avgLoss: number;
    maxDrawdown: number;
    avgRMultiple: number | null;
    bestTrade: number | null;
    worstTrade: number | null;
    avgHoldTimeMin: number | null;
    avgTradesPerDay: number;
  };
  timeOfDayPerformance: Record<string, { count: number; winRate: number; pnl: number }>;
  symbolPerformance: Record<string, { count: number; winRate: number; pnl: number }>;
  directionPerformance: { long: { count: number; winRate: number; pnl: number }; short: { count: number; winRate: number; pnl: number } };
}

/**
 * Deterministic pattern detection - runs before LLM.
 * The LLM gets these findings as input, not raw trades.
 * This keeps LLM output grounded in real data (zero hallucinations).
 */
export function analyzeTrades(trades: Trade[]): PatternAnalysis {
  const closed = trades.filter((t) => t.status === 'closed' && t.pnl !== null && t.pnl !== undefined);
  const wins = closed.filter((t) => (t.pnl ?? 0) > 0);
  const losses = closed.filter((t) => (t.pnl ?? 0) < 0);

  // Basic metrics
  const totalPnL = closed.reduce((s, t) => s + (t.pnl ?? 0), 0);
  const grossWin = wins.reduce((s, t) => s + (t.pnl ?? 0), 0);
  const grossLoss = Math.abs(losses.reduce((s, t) => s + (t.pnl ?? 0), 0));

  // Max drawdown (simple: peak-to-trough in equity curve)
  let maxDD = 0;
  let peak = 0;
  let equity = 0;
  const sortedByDate = [...closed].sort(
    (a, b) => (a.exitTime?.getTime() ?? 0) - (b.exitTime?.getTime() ?? 0)
  );
  for (const t of sortedByDate) {
    equity += t.pnl ?? 0;
    if (equity > peak) peak = equity;
    const dd = peak - equity;
    if (dd > maxDD) maxDD = dd;
  }

  // Hold time
  const holdTimes = closed
    .filter((t) => t.entryTime && t.exitTime)
    .map((t) => (t.exitTime!.getTime() - t.entryTime.getTime()) / 60000);
  const avgHoldTimeMin = holdTimes.length > 0
    ? holdTimes.reduce((s, m) => s + m, 0) / holdTimes.length
    : null;

  // Trades per day
  const daysCovered = sortedByDate.length >= 2
    ? Math.max(
      1,
      Math.ceil(
        ((sortedByDate[sortedByDate.length - 1].exitTime!.getTime() -
          sortedByDate[0].entryTime.getTime()) /
          (1000 * 60 * 60 * 24))
      )
    )
    : 1;
  const avgTradesPerDay = closed.length / daysCovered;

  // R-multiples
  const rMultiples = closed
    .map((t) => t.rMultiple)
    .filter((r): r is number => r !== null && r !== undefined && isFinite(r));
  const avgRMultiple = rMultiples.length > 0
    ? rMultiples.reduce((s, r) => s + r, 0) / rMultiples.length
    : null;

  // Best/worst
  const pnls = closed.map((t) => t.pnl ?? 0);
  const bestTrade = pnls.length > 0 ? Math.max(...pnls) : null;
  const worstTrade = pnls.length > 0 ? Math.min(...pnls) : null;

  // Time of day performance (by hour of entry)
  const timeOfDayPerformance: PatternAnalysis['timeOfDayPerformance'] = {};
  for (const t of closed) {
    const hour = t.entryTime.getHours();
    const bucket = `${hour.toString().padStart(2, '0')}:00`;
    if (!timeOfDayPerformance[bucket]) {
      timeOfDayPerformance[bucket] = { count: 0, winRate: 0, pnl: 0 };
    }
    timeOfDayPerformance[bucket].count++;
    timeOfDayPerformance[bucket].pnl += t.pnl ?? 0;
  }
  // Fill winRate per bucket
  for (const bucket of Object.keys(timeOfDayPerformance)) {
    const bucketTrades = closed.filter((t) => {
      const h = t.entryTime.getHours();
      return `${h.toString().padStart(2, '0')}:00` === bucket;
    });
    const bucketWins = bucketTrades.filter((t) => (t.pnl ?? 0) > 0).length;
    timeOfDayPerformance[bucket].winRate =
      bucketTrades.length > 0 ? (bucketWins / bucketTrades.length) * 100 : 0;
  }

  // Symbol performance
  const symbolPerformance: PatternAnalysis['symbolPerformance'] = {};
  for (const t of closed) {
    if (!symbolPerformance[t.symbol]) {
      symbolPerformance[t.symbol] = { count: 0, winRate: 0, pnl: 0 };
    }
    symbolPerformance[t.symbol].count++;
    symbolPerformance[t.symbol].pnl += t.pnl ?? 0;
  }
  for (const sym of Object.keys(symbolPerformance)) {
    const symTrades = closed.filter((t) => t.symbol === sym);
    const symWins = symTrades.filter((t) => (t.pnl ?? 0) > 0).length;
    symbolPerformance[sym].winRate = (symWins / symTrades.length) * 100;
  }

  // Direction performance
  const longs = closed.filter((t) => t.direction === 'long');
  const shorts = closed.filter((t) => t.direction === 'short');
  const directionPerformance = {
    long: {
      count: longs.length,
      winRate: longs.length > 0 ? (longs.filter((t) => (t.pnl ?? 0) > 0).length / longs.length) * 100 : 0,
      pnl: longs.reduce((s, t) => s + (t.pnl ?? 0), 0),
    },
    short: {
      count: shorts.length,
      winRate: shorts.length > 0 ? (shorts.filter((t) => (t.pnl ?? 0) > 0).length / shorts.length) * 100 : 0,
      pnl: shorts.reduce((s, t) => s + (t.pnl ?? 0), 0),
    },
  };

  // ===== Pattern detection =====
  const patterns: TradePattern[] = [];

  // 1. Revenge trading: trade entered within 10 min of a loss, with 2x+ normal size
  const avgSize = closed.length > 0
    ? closed.reduce((s, t) => s + (t.quantity ?? 0), 0) / closed.length
    : 0;
  const revengeTrades: Trade[] = [];
  const sortedAll = [...trades].sort((a, b) => a.entryTime.getTime() - b.entryTime.getTime());
  for (let i = 1; i < sortedAll.length; i++) {
    const prev = sortedAll[i - 1];
    const curr = sortedAll[i];
    if (
      prev.status === 'closed' &&
      (prev.pnl ?? 0) < 0 &&
      prev.exitTime &&
      curr.entryTime.getTime() - prev.exitTime.getTime() < 10 * 60 * 1000 &&
      curr.quantity > avgSize * 2
    ) {
      revengeTrades.push(curr);
    }
  }
  if (revengeTrades.length > 0) {
    patterns.push({
      type: 'revenge_trading',
      severity: Math.min(5, revengeTrades.length) as 1 | 2 | 3 | 4 | 5,
      count: revengeTrades.length,
      description: 'Trades opened soon after a loss with larger-than-normal size',
      evidence: `${revengeTrades.length} trades, avg size ${(
        revengeTrades.reduce((s, t) => s + t.quantity, 0) / revengeTrades.length
      ).toFixed(2)} vs normal ${avgSize.toFixed(2)}`,
      affectedTradeIds: revengeTrades.map((t) => t._id || '').filter(Boolean),
    });
  }

  // 2. Overtrading: days with > 3x median trades/day
  const tradesByDay = new Map<string, number>();
  for (const t of closed) {
    const dayKey = t.entryTime.toISOString().slice(0, 10);
    tradesByDay.set(dayKey, (tradesByDay.get(dayKey) || 0) + 1);
  }
  const counts = Array.from(tradesByDay.values()).sort((a, b) => a - b);
  const median = counts.length > 0 ? counts[Math.floor(counts.length / 2)] : 0;
  const overtradingDays = Array.from(tradesByDay.entries()).filter(
    ([, c]) => median > 0 && c >= median * 3 && c >= 5
  );
  if (overtradingDays.length > 0) {
    patterns.push({
      type: 'overtrading',
      severity: Math.min(5, overtradingDays.length) as 1 | 2 | 3 | 4 | 5,
      count: overtradingDays.length,
      description: 'Days with abnormally high trade count',
      evidence: overtradingDays
        .slice(0, 3)
        .map(([d, c]) => `${d}: ${c} trades`)
        .join('; '),
      affectedTradeIds: [],
    });
  }

  // 3. No stop loss on trades
  const noStopTrades = trades.filter((t) => !t.stopLoss && t.status !== 'pending');
  if (noStopTrades.length > closed.length * 0.2 && noStopTrades.length >= 3) {
    patterns.push({
      type: 'risk_management_slip',
      severity: 4,
      count: noStopTrades.length,
      description: 'Trades entered without a stop loss',
      evidence: `${noStopTrades.length} of ${trades.length} trades have no SL defined (${(
        (noStopTrades.length / trades.length) *
        100
      ).toFixed(0)}%)`,
      affectedTradeIds: noStopTrades.map((t) => t._id || '').filter(Boolean),
    });
  }

  // 4. Direction bias: >80% trades in one direction
  if (closed.length >= 10) {
    const longPct = (directionPerformance.long.count / closed.length) * 100;
    if (longPct > 80 || longPct < 20) {
      patterns.push({
        type: 'direction_bias',
        severity: 2,
        count: closed.length,
        description: `Heavy bias toward ${longPct > 80 ? 'long' : 'short'} direction`,
        evidence: `${longPct.toFixed(0)}% long vs ${(100 - longPct).toFixed(0)}% short`,
        affectedTradeIds: [],
      });
    }
  }

  // 5. Time of day edge (or disadvantage)
  if (closed.length >= 20) {
    const bucketEntries = Object.entries(timeOfDayPerformance)
      .filter(([, v]) => v.count >= 5)
      .sort((a, b) => b[1].pnl - a[1].pnl);

    if (bucketEntries.length >= 2) {
      const best = bucketEntries[0];
      const worst = bucketEntries[bucketEntries.length - 1];
      if (best[1].pnl - worst[1].pnl > Math.abs(totalPnL) * 0.3) {
        patterns.push({
          type: 'time_of_day_edge',
          severity: 3,
          count: best[1].count + worst[1].count,
          description: 'Performance varies significantly by time of day',
          evidence: `Best hour: ${best[0]} (${best[1].pnl.toFixed(2)} over ${best[1].count} trades). Worst hour: ${worst[0]} (${worst[1].pnl.toFixed(2)} over ${worst[1].count} trades).`,
          affectedTradeIds: [],
        });
      }
    }
  }

  // 6. Symbol bias (trades too concentrated)
  if (closed.length >= 10) {
    const symbolEntries = Object.entries(symbolPerformance).sort(
      (a, b) => b[1].count - a[1].count
    );
    if (symbolEntries.length > 0) {
      const topSymbol = symbolEntries[0];
      const topPct = (topSymbol[1].count / closed.length) * 100;
      if (topPct > 60 && symbolEntries.length > 2) {
        patterns.push({
          type: 'symbol_bias',
          severity: 2,
          count: topSymbol[1].count,
          description: `Overconcentration in ${topSymbol[0]}`,
          evidence: `${topPct.toFixed(0)}% of all trades on ${topSymbol[0]} (${topSymbol[1].count}/${closed.length})`,
          affectedTradeIds: [],
        });
      }
    }
  }

  // 7. Consistency (positive pattern)
  if (closed.length >= 20) {
    const dailyPnLs = Array.from(tradesByDay.keys())
      .map((day) => {
        const dayTrades = closed.filter((t) => t.entryTime.toISOString().slice(0, 10) === day);
        return dayTrades.reduce((s, t) => s + (t.pnl ?? 0), 0);
      });
    const winningDays = dailyPnLs.filter((p) => p > 0).length;
    const winningDayRate = (winningDays / dailyPnLs.length) * 100;
    if (winningDayRate > 60) {
      patterns.push({
        type: 'consistency',
        severity: 1,
        count: winningDays,
        description: 'Strong consistency in daily profitability',
        evidence: `${winningDayRate.toFixed(0)}% of trading days are profitable (${winningDays}/${dailyPnLs.length})`,
        affectedTradeIds: [],
      });
    }
  }

  return {
    patterns,
    metrics: {
      totalTrades: trades.length,
      closedTrades: closed.length,
      winRate: closed.length > 0 ? (wins.length / closed.length) * 100 : 0,
      totalPnL,
      profitFactor: grossLoss > 0 ? grossWin / grossLoss : wins.length > 0 ? 999 : 0,
      avgWin: wins.length > 0 ? grossWin / wins.length : 0,
      avgLoss: losses.length > 0 ? -grossLoss / losses.length : 0,
      maxDrawdown: maxDD,
      avgRMultiple,
      bestTrade,
      worstTrade,
      avgHoldTimeMin,
      avgTradesPerDay,
    },
    timeOfDayPerformance,
    symbolPerformance,
    directionPerformance,
  };
}
