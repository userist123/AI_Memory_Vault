import { getUserById } from '@/lib/db/users';
import { getUsage, incrementUsage, type UsageFeature } from '@/lib/db/usage';
import { getPlan, type PlanId, type PlanLimits } from '@/lib/billing/plans';

export interface QuotaCheckResult {
  allowed: boolean;
  plan: PlanId;
  used: number;
  limit: number; // -1 = unlimited
  period: 'day' | 'month';
  feature: UsageFeature;
  message?: string;
}

/**
 * Map features to usage tracking keys + plan limit fields
 */
const FEATURE_CONFIG: Record<
  UsageFeature,
  { period: 'day' | 'month'; limitKey: keyof PlanLimits; displayName: { ro: string; en: string } }
> = {
  tradeImport: {
    period: 'month',
    limitKey: 'maxTradesPerMonth',
    displayName: {
      ro: 'Tranzacții importate',
      en: 'Trades imported',
    },
  },
  voiceJournal: {
    period: 'day',
    limitKey: 'maxVoiceJournalsPerDay',
    displayName: {
      ro: 'Jurnale vocale',
      en: 'Voice journals',
    },
  },
  tradeReview: {
    period: 'month',
    limitKey: 'maxTradeReviewsPerMonth',
    displayName: {
      ro: 'Analize AI per tranzacție',
      en: 'AI trade reviews',
    },
  },
  coachReport: {
    period: 'month',
    limitKey: 'maxCoachReportsPerMonth',
    displayName: {
      ro: 'Rapoarte coach AI',
      en: 'AI coach reports',
    },
  },
  marketScanner: {
    period: 'month',
    limitKey: 'maxTradeReviewsPerMonth',
    displayName: {
      ro: 'Scanări piață',
      en: 'Market scans',
    },
  },
};

/**
 * Check if user has quota for an action.
 * Returns detailed result; does NOT increment.
 */
export async function checkQuota(
  userId: string,
  feature: UsageFeature
): Promise<QuotaCheckResult> {
  const user = await getUserById(userId);
  if (!user) {
    return {
      allowed: false,
      plan: 'free',
      used: 0,
      limit: 0,
      period: 'day',
      feature,
      message: 'User not found',
    };
  }

  const plan = getPlan(user.plan);
  const config = FEATURE_CONFIG[feature];
  const limit = plan.limits[config.limitKey] as number;

  // Boolean features (like marketScanner) - check via feature flag
  if (feature === 'marketScanner' && !plan.limits.aiMarketScanner) {
    return {
      allowed: false,
      plan: user.plan,
      used: 0,
      limit: 0,
      period: config.period,
      feature,
      message: 'Market scanner requires Elite plan',
    };
  }

  const used = await getUsage(userId, feature, config.period);

  if (limit === -1) {
    return {
      allowed: true,
      plan: user.plan,
      used,
      limit: -1,
      period: config.period,
      feature,
    };
  }

  const allowed = used < limit;
  return {
    allowed,
    plan: user.plan,
    used,
    limit,
    period: config.period,
    feature,
    message: allowed
      ? undefined
      : `Quota exceeded: ${used}/${limit} ${config.period === 'day' ? 'per day' : 'per month'}. Upgrade your plan.`,
  };
}

/**
 * Check + increment atomically.
 * Use this right before performing the expensive action.
 */
export async function consumeQuota(
  userId: string,
  feature: UsageFeature
): Promise<QuotaCheckResult> {
  const check = await checkQuota(userId, feature);
  if (!check.allowed) return check;

  const config = FEATURE_CONFIG[feature];
  const newCount = await incrementUsage(userId, feature, config.period, 1);

  return {
    ...check,
    used: newCount,
  };
}

/**
 * Standardized 402 response for quota exceeded
 */
export function quotaExceededResponse(
  check: QuotaCheckResult,
  language: 'ro' | 'en' = 'ro'
): { body: Record<string, unknown>; status: number } {
  const config = FEATURE_CONFIG[check.feature];
  const featureName = config.displayName[language];

  const msg = language === 'ro'
    ? `Ai atins limita pentru planul ${check.plan.toUpperCase()}: ${check.used}/${check.limit} ${featureName} ${check.period === 'day' ? 'pe zi' : 'pe lună'}. Upgrade la Pro sau Elite pentru mai mult.`
    : `${check.plan.toUpperCase()} plan limit reached: ${check.used}/${check.limit} ${featureName} ${check.period === 'day' ? 'per day' : 'per month'}. Upgrade to Pro or Elite.`;

  return {
    body: {
      error: 'quota_exceeded',
      plan: check.plan,
      feature: check.feature,
      used: check.used,
      limit: check.limit,
      period: check.period,
      message: msg,
      upgradeUrl: '/pricing',
    },
    status: 402, // 402 Payment Required
  };
}
