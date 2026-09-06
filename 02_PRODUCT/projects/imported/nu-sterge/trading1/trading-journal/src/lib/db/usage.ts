import { Collection } from 'mongodb';
import { getDb } from './mongo';

export type UsageFeature =
  | 'tradeImport'
  | 'voiceJournal'
  | 'tradeReview'
  | 'coachReport'
  | 'marketScanner';

export interface UsageRecord {
  _id?: string;
  userId: string;
  feature: UsageFeature;
  period: 'day' | 'month';
  periodKey: string; // YYYY-MM-DD or YYYY-MM
  count: number;
  lastIncrementAt: Date;
}

async function getUsageCol(): Promise<Collection<UsageRecord> | null> {
  const db = await getDb();
  if (!db) return null;
  const col = db.collection<UsageRecord>('usage');
  try {
    await col.createIndex(
      { userId: 1, feature: 1, period: 1, periodKey: 1 },
      { unique: true }
    );
    // TTL - cleanup old records after 90 days
    await col.createIndex({ lastIncrementAt: 1 }, { expireAfterSeconds: 90 * 24 * 60 * 60 });
  } catch {}
  return col;
}

// In-memory fallback
const memUsage = new Map<string, UsageRecord>();

function getDayKey(date: Date = new Date()): string {
  return date.toISOString().slice(0, 10);
}

function getMonthKey(date: Date = new Date()): string {
  return date.toISOString().slice(0, 7);
}

function recordKey(userId: string, feature: UsageFeature, period: 'day' | 'month', periodKey: string): string {
  return `${userId}:${feature}:${period}:${periodKey}`;
}

/**
 * Get current usage count for user/feature/period
 */
export async function getUsage(
  userId: string,
  feature: UsageFeature,
  period: 'day' | 'month'
): Promise<number> {
  const periodKey = period === 'day' ? getDayKey() : getMonthKey();
  const col = await getUsageCol();

  if (col) {
    const record = await col.findOne({ userId, feature, period, periodKey });
    return record?.count || 0;
  }

  const key = recordKey(userId, feature, period, periodKey);
  return memUsage.get(key)?.count || 0;
}

/**
 * Increment usage counter atomically.
 * Returns new count.
 */
export async function incrementUsage(
  userId: string,
  feature: UsageFeature,
  period: 'day' | 'month',
  by: number = 1
): Promise<number> {
  const periodKey = period === 'day' ? getDayKey() : getMonthKey();
  const now = new Date();
  const col = await getUsageCol();

  if (col) {
    const result = await col.findOneAndUpdate(
      { userId, feature, period, periodKey },
      {
        $inc: { count: by },
        $set: { lastIncrementAt: now },
        $setOnInsert: { userId, feature, period, periodKey },
      },
      { upsert: true, returnDocument: 'after' }
    );
    return result?.count || by;
  }

  const key = recordKey(userId, feature, period, periodKey);
  const existing = memUsage.get(key);
  const newCount = (existing?.count || 0) + by;
  memUsage.set(key, {
    userId,
    feature,
    period,
    periodKey,
    count: newCount,
    lastIncrementAt: now,
  });
  return newCount;
}

/**
 * Get all usage for a user for current period (useful for UI)
 */
export async function getAllUsage(userId: string): Promise<{
  daily: Record<string, number>;
  monthly: Record<string, number>;
}> {
  const dayKey = getDayKey();
  const monthKey = getMonthKey();
  const col = await getUsageCol();

  const daily: Record<string, number> = {};
  const monthly: Record<string, number> = {};

  if (col) {
    const [dailyRecords, monthlyRecords] = await Promise.all([
      col.find({ userId, period: 'day', periodKey: dayKey }).toArray(),
      col.find({ userId, period: 'month', periodKey: monthKey }).toArray(),
    ]);
    dailyRecords.forEach((r) => { daily[r.feature] = r.count; });
    monthlyRecords.forEach((r) => { monthly[r.feature] = r.count; });
    return { daily, monthly };
  }

  // In-memory
  memUsage.forEach((r) => {
    if (r.userId !== userId) return;
    if (r.period === 'day' && r.periodKey === dayKey) {
      daily[r.feature] = r.count;
    } else if (r.period === 'month' && r.periodKey === monthKey) {
      monthly[r.feature] = r.count;
    }
  });

  return { daily, monthly };
}
