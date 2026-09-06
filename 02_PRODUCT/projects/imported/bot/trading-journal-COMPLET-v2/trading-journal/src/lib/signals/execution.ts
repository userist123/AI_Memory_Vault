import type { Signal } from '@/lib/signals/detector';
import type { BrokerId, OrderRequest } from '@/lib/brokers/types';
import { getAdapter, getBrokerCredentials } from '@/lib/brokers';
import { calculatePositionSize } from '@/lib/signals/detector';
import { checkRiskRules } from '@/lib/signals/risk-guards';
import { updateAlertStatus } from '@/lib/db/alerts';
import { saveTradesBatch, getTradesByUser } from '@/lib/db/mongo';
import type { Trade } from '@/types/trade';

export interface ExecutionRequest {
  userId: string;
  signalId: string;
  signal: Signal;
  brokerId: BrokerId;
  testnet: boolean;
  // Risk override (optional - default from user's rules)
  riskPercentOverride?: number;
  // User's confirmed reason
  reason: string;
}

export interface ExecutionResult {
  success: boolean;
  error?: string;
  blockedByRisk?: boolean;
  warnings?: string[];
  order?: {
    brokerOrderId: string;
    status: string;
    filledQuantity: number;
    avgFillPrice?: number;
  };
  tradeId?: string;
}

/**
 * Execute a signal end-to-end with full validation.
 *
 * Flow:
 * 1. Load broker credentials (decrypted)
 * 2. Get account balance from broker
 * 3. Calculate position size from risk %
 * 4. Run risk guards (max trades/day, daily loss, etc.)
 * 5. Place order on broker
 * 6. Record as Trade in DB
 * 7. Update alert status
 */
export async function executeSignal(req: ExecutionRequest): Promise<ExecutionResult> {
  const { userId, signalId, signal, brokerId, testnet, reason } = req;

  // 1. Load broker credentials
  const creds = await getBrokerCredentials(userId, brokerId, testnet);
  if (!creds) {
    return {
      success: false,
      error: `Broker ${brokerId} ${testnet ? '(testnet)' : ''} nu e conectat. Conectează-l la /settings.`,
    };
  }

  const adapter = getAdapter(brokerId);

  // 2. Get account balance
  let accountBalance = 0;
  try {
    const balances = await adapter.getBalances({
      apiKey: creds.apiKey,
      apiSecret: creds.apiSecret,
      testnet,
    });
    // For crypto brokers, use USDT/USDC balance as "account size"
    const usdStable = balances.find((b) => ['USDT', 'USDC', 'BUSD', 'DAI'].includes(b.currency));
    const usdFiat = balances.find((b) => ['USD', 'EUR'].includes(b.currency));
    accountBalance = usdStable?.total ?? usdFiat?.total ?? 0;
  } catch (err: unknown) {
    const e = err as { message?: string };
    return { success: false, error: `Nu pot citi balanța: ${e.message}` };
  }

  if (accountBalance <= 0) {
    return { success: false, error: 'Cont gol sau fără cash disponibil' };
  }

  // 3. Calculate position size
  const riskPct = req.riskPercentOverride ?? 1; // default 1%
  const sizing = calculatePositionSize(accountBalance, riskPct, signal.entry, signal.stopLoss);

  if (sizing.quantity <= 0) {
    return { success: false, error: 'Poziție calculată = 0. Verifică entry și stop loss.' };
  }

  // 4. Risk guards - need recent trades context
  const since = new Date();
  since.setHours(0, 0, 0, 0);
  const todaysTrades = await getTradesByUser(userId, { since, limit: 100 });
  const todaysPnl = todaysTrades.reduce((s, t) => s + (t.pnl ?? 0), 0);
  const openPositions = todaysTrades.filter((t) => t.status === 'open').length;

  // Recent losses for revenge trading check
  const recentTrades = await getTradesByUser(userId, { limit: 5 });
  const recentClosed = recentTrades.filter((t) => t.status === 'closed' && t.pnl !== null);
  let recentLosses = 0;
  let lastLossTime: Date | undefined;
  for (const t of recentClosed) {
    if ((t.pnl ?? 0) < 0) {
      recentLosses++;
      if (!lastLossTime && t.exitTime) lastLossTime = t.exitTime instanceof Date ? t.exitTime : new Date(t.exitTime);
    } else {
      break; // Streak broken
    }
  }

  const riskCheck = await checkRiskRules({
    userId,
    accountBalance,
    entry: signal.entry,
    stopLoss: signal.stopLoss,
    quantity: sizing.quantity,
    hasReason: reason.trim().length > 0,
    todaysTrades: todaysTrades.length,
    todaysPnl,
    openPositions,
    recentLossesCount: recentLosses,
    lastLossTime,
  });

  if (!riskCheck.allowed) {
    return {
      success: false,
      blockedByRisk: true,
      error: riskCheck.blockReason,
      warnings: riskCheck.warnings,
    };
  }

  // Cap quantity at max allowed (risk guard may reduce)
  const finalQuantity = riskCheck.maxAllowedQuantity
    ? Math.min(sizing.quantity, riskCheck.maxAllowedQuantity)
    : sizing.quantity;

  // 5. Place order on broker
  const orderRequest: OrderRequest = {
    symbol: signal.symbol,
    side: signal.direction === 'long' ? 'buy' : 'sell',
    type: 'market', // market entry, SL/TP set as separate orders after
    quantity: finalQuantity,
    clientOrderId: `tj_${signalId}_${Date.now()}`,
  };

  let placedOrder;
  try {
    placedOrder = await adapter.placeOrder(
      { apiKey: creds.apiKey, apiSecret: creds.apiSecret, testnet },
      orderRequest
    );
  } catch (err: unknown) {
    const e = err as { message?: string };
    return { success: false, error: `Ordinul a eșuat la broker: ${e.message}` };
  }

  // 6. Set stop loss order (separate order for spot)
  // NOTE: Binance Spot doesn't support OCO natively for market-filled positions without extra setup.
  // For MVP: we record SL/TP as "planned" and rely on user to monitor + our alert system.
  // Future: OCO orders on Binance when position is filled.
  //
  // For simplicity, we save the trade with SL/TP as metadata - user can set SL manually or we
  // can implement server-side monitoring (cron job).

  // 7. Save as Trade
  const tradesForDb = [{
    userId,
    externalId: placedOrder.brokerOrderId,
    broker: brokerId,
    accountId: null,
    symbol: signal.symbol,
    assetClass: 'crypto' as const,
    direction: signal.direction,
    status: placedOrder.status === 'filled' ? ('open' as const) : ('pending' as const),
    entryPrice: placedOrder.avgFillPrice ?? signal.entry,
    exitPrice: null,
    stopLoss: signal.stopLoss,
    takeProfit: signal.takeProfit,
    quantity: placedOrder.filledQuantity || finalQuantity,
    lotSize: null,
    entryTime: placedOrder.submittedAt,
    exitTime: null,
    pnl: null,
    pnlPercent: null,
    commission: placedOrder.commission ?? 0,
    swap: 0,
    currency: 'USD' as const,
    rMultiple: null,
    strategy: signal.type,
    tags: ['ai_signal', signal.type],
    notes: `${reason}\n---\nAI Signal: ${signal.reason}`,
    screenshots: [],
    importSource: `signal_${signalId}`,
    importBatch: null,
  }];

  const { inserted, savedTrades } = await saveTradesBatch(tradesForDb);
  const tradeId = savedTrades[0]?._id;

  // 8. Update alert status
  await updateAlertStatus(signalId, 'executed', {
    executionDetails: {
      brokerId,
      brokerOrderId: placedOrder.brokerOrderId,
      filledQuantity: placedOrder.filledQuantity,
      avgFillPrice: placedOrder.avgFillPrice ?? signal.entry,
      commission: placedOrder.commission ?? 0,
    },
  });

  return {
    success: true,
    warnings: riskCheck.warnings,
    order: {
      brokerOrderId: placedOrder.brokerOrderId,
      status: placedOrder.status,
      filledQuantity: placedOrder.filledQuantity,
      avgFillPrice: placedOrder.avgFillPrice,
    },
    tradeId,
  };
}
