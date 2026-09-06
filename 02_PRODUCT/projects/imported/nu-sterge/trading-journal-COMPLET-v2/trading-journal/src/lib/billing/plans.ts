export type PlanId = 'free' | 'pro' | 'elite' | 'autopilot';

export interface PlanLimits {
  // Trades
  maxTradesPerMonth: number; // -1 = unlimited
  maxBrokerConnections: number;

  // AI features
  maxVoiceJournalsPerDay: number;
  maxTradeReviewsPerMonth: number;
  maxCoachReportsPerMonth: number;

  // Signals & execution (Pas 9)
  maxSignalsPerMonth: number;
  maxExecutionsPerMonth: number;
  maxBacktestsPerMonth: number;

  // Consulting (Pas 10)
  consultingSessionsPerMonth: number; // free sessions included
  consultingDiscountPct: number; // discount on paid sessions

  // Advanced
  semanticSearch: boolean;
  mt5DesktopBridge: boolean;
  aiMarketScanner: boolean;
  apiAccess: boolean;

  // Romania-specific
  fiscalModuleFull: boolean;
  fiscalChatAI: boolean;

  // Priority
  priorityAI: boolean;
}

export interface PlanInfo {
  id: PlanId;
  name: string;
  limits: PlanLimits;
  prices: {
    monthly: { usd: number; ron: number };
    yearly: { usd: number; ron: number };
  };
  // Polar product IDs - set via env in production
  polarProductIds?: {
    monthly?: string;
    yearly?: string;
  };
}

export const PLANS: Record<PlanId, PlanInfo> = {
  free: {
    id: 'free',
    name: 'Free',
    limits: {
      maxTradesPerMonth: 50,
      maxBrokerConnections: 1,
      maxVoiceJournalsPerDay: 3,
      maxTradeReviewsPerMonth: 10,
      maxCoachReportsPerMonth: 4,
      maxSignalsPerMonth: 10,
      maxExecutionsPerMonth: 0, // no execution on free
      maxBacktestsPerMonth: 2,
      consultingSessionsPerMonth: 0,
      consultingDiscountPct: 0,
      semanticSearch: false,
      mt5DesktopBridge: false,
      aiMarketScanner: false,
      apiAccess: false,
      fiscalModuleFull: false,
      fiscalChatAI: false,
      priorityAI: false,
    },
    prices: {
      monthly: { usd: 0, ron: 0 },
      yearly: { usd: 0, ron: 0 },
    },
  },
  pro: {
    id: 'pro',
    name: 'Pro',
    limits: {
      maxTradesPerMonth: -1,
      maxBrokerConnections: 3,
      maxVoiceJournalsPerDay: -1,
      maxTradeReviewsPerMonth: 100,
      maxCoachReportsPerMonth: 30,
      maxSignalsPerMonth: 100,
      maxExecutionsPerMonth: 0, // no execution on pro - upgrade to elite
      maxBacktestsPerMonth: 20,
      consultingSessionsPerMonth: 0,
      consultingDiscountPct: 10, // 10% discount on paid sessions
      semanticSearch: true,
      mt5DesktopBridge: false,
      aiMarketScanner: false,
      apiAccess: false,
      fiscalModuleFull: true,
      fiscalChatAI: true,
      priorityAI: false,
    },
    prices: {
      monthly: { usd: 15, ron: 35 },
      yearly: { usd: 129, ron: 299 },
    },
    polarProductIds: {
      monthly: process.env.POLAR_PRO_MONTHLY_ID,
      yearly: process.env.POLAR_PRO_YEARLY_ID,
    },
  },
  elite: {
    id: 'elite',
    name: 'Elite',
    limits: {
      maxTradesPerMonth: -1,
      maxBrokerConnections: -1,
      maxVoiceJournalsPerDay: -1,
      maxTradeReviewsPerMonth: -1,
      maxCoachReportsPerMonth: -1,
      maxSignalsPerMonth: -1,
      maxExecutionsPerMonth: 10, // 10 one-click trades/month
      maxBacktestsPerMonth: -1,
      consultingSessionsPerMonth: 0,
      consultingDiscountPct: 20,
      semanticSearch: true,
      mt5DesktopBridge: true,
      aiMarketScanner: true,
      apiAccess: true,
      fiscalModuleFull: true,
      fiscalChatAI: true,
      priorityAI: true,
    },
    prices: {
      monthly: { usd: 29, ron: 69 },
      yearly: { usd: 249, ron: 599 },
    },
    polarProductIds: {
      monthly: process.env.POLAR_ELITE_MONTHLY_ID,
      yearly: process.env.POLAR_ELITE_YEARLY_ID,
    },
  },
  autopilot: {
    id: 'autopilot',
    name: 'AutoPilot',
    limits: {
      maxTradesPerMonth: -1,
      maxBrokerConnections: -1,
      maxVoiceJournalsPerDay: -1,
      maxTradeReviewsPerMonth: -1,
      maxCoachReportsPerMonth: -1,
      maxSignalsPerMonth: -1,
      maxExecutionsPerMonth: -1, // UNLIMITED execution
      maxBacktestsPerMonth: -1,
      consultingSessionsPerMonth: 1, // 1 free 1-on-1 session per month (with owner)
      consultingDiscountPct: 30,
      semanticSearch: true,
      mt5DesktopBridge: true,
      aiMarketScanner: true,
      apiAccess: true,
      fiscalModuleFull: true,
      fiscalChatAI: true,
      priorityAI: true,
    },
    prices: {
      monthly: { usd: 55, ron: 129 },
      yearly: { usd: 499, ron: 1149 },
    },
    polarProductIds: {
      monthly: process.env.POLAR_AUTOPILOT_MONTHLY_ID,
      yearly: process.env.POLAR_AUTOPILOT_YEARLY_ID,
    },
  },
};

export function getPlan(planId: PlanId): PlanInfo {
  return PLANS[planId] || PLANS.free;
}

export function isFeatureEnabled(
  planId: PlanId,
  feature: keyof PlanLimits
): boolean {
  const plan = getPlan(planId);
  const value = plan.limits[feature];
  if (typeof value === 'boolean') return value;
  return value === -1 || value > 0;
}

export function hasQuota(
  planId: PlanId,
  feature: keyof PlanLimits,
  currentUsage: number
): boolean {
  const plan = getPlan(planId);
  const limit = plan.limits[feature];
  if (typeof limit !== 'number') return false;
  if (limit === -1) return true; // unlimited
  return currentUsage < limit;
}
