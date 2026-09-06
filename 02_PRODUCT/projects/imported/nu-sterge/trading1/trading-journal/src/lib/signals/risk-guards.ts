import { Collection } from 'mongodb';
import { getDb } from '@/lib/db/mongo';

export interface RiskRules {
  userId: string;
  maxTradesPerDay: number; // default 3
  maxRiskPerTradePct: number; // default 2%
  maxDailyLossPct: number; // default 5% (stops trading for the day)
  maxOpenPositions: number; // default 3
  // Anti-revenge: after N consecutive losses, block for cooldownMin
  consecutiveLossBlockThreshold: number; // default 2
  cooldownMinutes: number; // default 1440 (24h)
  // Require reason for each trade
  requireReason: boolean; // default true
  // Mandatory SL
  requireStopLoss: boolean; // default true
  // Max leverage (if applicable)
  maxLeverage: number; // default 1 (spot only)
  updatedAt: Date;
}

export const DEFAULT_RISK_RULES: Omit<RiskRules, 'userId' | 'updatedAt'> = {
  maxTradesPerDay: 3,
  maxRiskPerTradePct: 2,
  maxDailyLossPct: 5,
  maxOpenPositions: 3,
  consecutiveLossBlockThreshold: 2,
  cooldownMinutes: 1440,
  requireReason: true,
  requireStopLoss: true,
  maxLeverage: 1,
};

export interface RiskCheckResult {
  allowed: boolean;
  blockReason?: string;
  warnings: string[];
  maxAllowedQuantity?: number;
}

async function getRulesCol(): Promise<Collection<RiskRules> | null> {
  const db = await getDb();
  if (!db) return null;
  const col = db.collection<RiskRules>('risk_rules');
  try {
    await col.createIndex({ userId: 1 }, { unique: true });
  } catch {}
  return col;
}

const memRules = new Map<string, RiskRules>();

export async function getRiskRules(userId: string): Promise<RiskRules> {
  const col = await getRulesCol();
  if (col) {
    const rules = await col.findOne({ userId });
    if (rules) return rules;
  } else {
    const cached = memRules.get(userId);
    if (cached) return cached;
  }
  // Return defaults
  return { ...DEFAULT_RISK_RULES, userId, updatedAt: new Date() };
}

export async function updateRiskRules(
  userId: string,
  updates: Partial<Omit<RiskRules, 'userId' | 'updatedAt'>>
): Promise<RiskRules> {
  const current = await getRiskRules(userId);
  const updated: RiskRules = { ...current, ...updates, userId, updatedAt: new Date() };

  const col = await getRulesCol();
  if (col) {
    await col.findOneAndUpdate(
      { userId },
      { $set: updated },
      { upsert: true }
    );
  } else {
    memRules.set(userId, updated);
  }

  return updated;
}

interface TradeCheckParams {
  userId: string;
  accountBalance: number;
  entry: number;
  stopLoss?: number;
  quantity: number;
  hasReason: boolean;
  todaysTrades: number;
  todaysPnl: number; // negative if losing
  openPositions: number;
  recentLossesCount: number; // last N trades that were losses (for cooldown check)
  lastLossTime?: Date;
}

/**
 * Run all risk checks before a trade.
 * Returns { allowed: false } if ANY hard rule is violated.
 */
export async function checkRiskRules(params: TradeCheckParams): Promise<RiskCheckResult> {
  const rules = await getRiskRules(params.userId);
  const warnings: string[] = [];

  // 1. Max trades per day
  if (params.todaysTrades >= rules.maxTradesPerDay) {
    return {
      allowed: false,
      blockReason: `Ai atins limita de ${rules.maxTradesPerDay} trades/zi. Ai nevoie de odihnă.`,
      warnings,
    };
  }

  // 2. Daily loss circuit breaker
  const dailyLossPct = (params.todaysPnl / params.accountBalance) * 100;
  if (dailyLossPct < -rules.maxDailyLossPct) {
    return {
      allowed: false,
      blockReason: `Ai pierdut ${Math.abs(dailyLossPct).toFixed(1)}% azi. Trading blocat până mâine (limită ${rules.maxDailyLossPct}%).`,
      warnings,
    };
  }

  // 3. Max open positions
  if (params.openPositions >= rules.maxOpenPositions) {
    return {
      allowed: false,
      blockReason: `Ai deja ${params.openPositions} poziții deschise. Limită: ${rules.maxOpenPositions}.`,
      warnings,
    };
  }

  // 4. Revenge trading cooldown
  if (
    params.recentLossesCount >= rules.consecutiveLossBlockThreshold &&
    params.lastLossTime
  ) {
    const minutesSince = (Date.now() - params.lastLossTime.getTime()) / 60000;
    if (minutesSince < rules.cooldownMinutes) {
      const remainingMin = Math.ceil(rules.cooldownMinutes - minutesSince);
      return {
        allowed: false,
        blockReason: `Protecție revenge trading: ${rules.consecutiveLossBlockThreshold} pierderi consecutive. Așteaptă ${remainingMin} minute.`,
        warnings,
      };
    }
  }

  // 5. Mandatory stop loss
  if (rules.requireStopLoss && !params.stopLoss) {
    return {
      allowed: false,
      blockReason: 'Stop loss obligatoriu. Setează SL înainte de a deschide poziția.',
      warnings,
    };
  }

  // 6. Mandatory reason
  if (rules.requireReason && !params.hasReason) {
    return {
      allowed: false,
      blockReason: 'Motivul trade-ului obligatoriu. De ce intri?',
      warnings,
    };
  }

  // 7. Risk per trade check
  let maxAllowedQuantity: number | undefined;
  if (params.stopLoss) {
    const maxRiskAmount = params.accountBalance * (rules.maxRiskPerTradePct / 100);
    const priceDiff = Math.abs(params.entry - params.stopLoss);
    if (priceDiff > 0) {
      maxAllowedQuantity = maxRiskAmount / priceDiff;
      if (params.quantity > maxAllowedQuantity) {
        return {
          allowed: false,
          blockReason: `Poziție prea mare. Max ${maxAllowedQuantity.toFixed(6)} la ${rules.maxRiskPerTradePct}% risk. Reduci sau mărești SL.`,
          warnings,
          maxAllowedQuantity,
        };
      }
    }
  }

  // ===== Soft warnings (allowed but heads up) =====
  if (params.todaysTrades >= rules.maxTradesPerDay - 1) {
    warnings.push(`Atenție: mai ai 1 trade disponibil azi (limită ${rules.maxTradesPerDay}).`);
  }

  if (dailyLossPct < -(rules.maxDailyLossPct * 0.7)) {
    warnings.push(`Azi ai pierdut ${Math.abs(dailyLossPct).toFixed(1)}%. Aproape de circuit breaker ${rules.maxDailyLossPct}%.`);
  }

  if (params.recentLossesCount >= 1) {
    warnings.push(`Ultima tranzacție a fost pierdere. Atenție la revenge trading.`);
  }

  return { allowed: true, warnings, maxAllowedQuantity };
}
