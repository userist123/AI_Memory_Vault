import { Collection, ObjectId } from 'mongodb';
import { getDb } from './mongo';
import type { Signal, SignalType } from '@/lib/signals/detector';

export interface StoredAlert {
  _id?: string;
  userId: string;
  signalId: string; // unique per signal
  signal: Signal;
  createdAt: Date;
  expiresAt: Date; // alerts expire after 24h
  // User action
  status: 'pending' | 'executed' | 'skipped' | 'expired';
  executedAt?: Date;
  executionDetails?: {
    brokerId: string;
    brokerOrderId: string;
    filledQuantity: number;
    avgFillPrice: number;
    commission: number;
  };
  skippedAt?: Date;
  skipReason?: string;
  // Delivery tracking
  deliveredVia: ('email' | 'telegram' | 'in_app')[];
}

async function getAlertsCol(): Promise<Collection<StoredAlert> | null> {
  const db = await getDb();
  if (!db) return null;
  const col = db.collection<StoredAlert>('alerts');
  try {
    await col.createIndex({ userId: 1, createdAt: -1 });
    await col.createIndex({ signalId: 1 }, { unique: true });
    await col.createIndex({ status: 1, expiresAt: 1 });
    // Auto-expire
    await col.createIndex({ expiresAt: 1 }, { expireAfterSeconds: 0 });
  } catch {}
  return col;
}

const memAlerts = new Map<string, StoredAlert>();

export async function saveAlert(alert: Omit<StoredAlert, '_id'>): Promise<string> {
  const col = await getAlertsCol();
  if (col) {
    const result = await col.insertOne(alert as StoredAlert);
    return result.insertedId.toString();
  }
  const id = alert.signalId;
  memAlerts.set(id, { ...alert, _id: id });
  return id;
}

export async function getAlert(signalId: string): Promise<StoredAlert | null> {
  const col = await getAlertsCol();
  if (col) {
    return col.findOne({ signalId });
  }
  return memAlerts.get(signalId) || null;
}

export async function getAlertById(id: string): Promise<StoredAlert | null> {
  const col = await getAlertsCol();
  if (col) {
    try {
      if (ObjectId.isValid(id)) {
        return col.findOne({ _id: new ObjectId(id) } as unknown as Record<string, unknown>);
      }
    } catch {}
    return col.findOne({ signalId: id });
  }
  return memAlerts.get(id) || null;
}

export async function getUserAlerts(
  userId: string,
  options: { status?: StoredAlert['status']; limit?: number } = {}
): Promise<StoredAlert[]> {
  const { limit = 50, status } = options;
  const col = await getAlertsCol();
  const filter: Record<string, unknown> = { userId };
  if (status) filter.status = status;

  if (col) {
    return col.find(filter).sort({ createdAt: -1 }).limit(limit).toArray();
  }

  return Array.from(memAlerts.values())
    .filter((a) => a.userId === userId && (!status || a.status === status))
    .sort((a, b) => b.createdAt.getTime() - a.createdAt.getTime())
    .slice(0, limit);
}

export async function updateAlertStatus(
  signalId: string,
  status: StoredAlert['status'],
  details?: Partial<StoredAlert>
): Promise<boolean> {
  const col = await getAlertsCol();
  const updates: Record<string, unknown> = { status, ...details };
  if (status === 'executed') updates.executedAt = new Date();
  if (status === 'skipped') updates.skippedAt = new Date();

  if (col) {
    const result = await col.updateOne({ signalId }, { $set: updates });
    return result.modifiedCount > 0;
  }

  const alert = memAlerts.get(signalId);
  if (!alert) return false;
  memAlerts.set(signalId, { ...alert, ...updates, status });
  return true;
}

// ===== User notification preferences =====

export interface NotificationPrefs {
  userId: string;
  email: {
    enabled: boolean;
    address: string; // defaults to account email
  };
  telegram: {
    enabled: boolean;
    chatId?: string;
    linkedAt?: Date;
  };
  inApp: {
    enabled: boolean;
  };
  // What to alert on
  filters: {
    minStrength: number; // e.g. 70
    minRiskReward: number; // e.g. 2
    symbols?: string[]; // empty = all
    signalTypes?: SignalType[]; // empty = all
  };
  updatedAt: Date;
}

async function getPrefsCol(): Promise<Collection<NotificationPrefs> | null> {
  const db = await getDb();
  if (!db) return null;
  const col = db.collection<NotificationPrefs>('notification_prefs');
  try {
    await col.createIndex({ userId: 1 }, { unique: true });
    await col.createIndex({ 'telegram.chatId': 1 });
  } catch {}
  return col;
}

const memPrefs = new Map<string, NotificationPrefs>();

export async function getNotificationPrefs(userId: string): Promise<NotificationPrefs> {
  const col = await getPrefsCol();
  if (col) {
    const prefs = await col.findOne({ userId });
    if (prefs) return prefs;
  } else {
    const cached = memPrefs.get(userId);
    if (cached) return cached;
  }
  // Default
  return {
    userId,
    email: { enabled: true, address: '' },
    telegram: { enabled: false },
    inApp: { enabled: true },
    filters: { minStrength: 65, minRiskReward: 1.5 },
    updatedAt: new Date(),
  };
}

export async function updateNotificationPrefs(
  userId: string,
  updates: Partial<Omit<NotificationPrefs, 'userId' | 'updatedAt'>>
): Promise<NotificationPrefs> {
  const current = await getNotificationPrefs(userId);
  const merged: NotificationPrefs = {
    ...current,
    ...updates,
    email: { ...current.email, ...(updates.email || {}) },
    telegram: { ...current.telegram, ...(updates.telegram || {}) },
    inApp: { ...current.inApp, ...(updates.inApp || {}) },
    filters: { ...current.filters, ...(updates.filters || {}) },
    userId,
    updatedAt: new Date(),
  };

  const col = await getPrefsCol();
  if (col) {
    await col.findOneAndUpdate({ userId }, { $set: merged }, { upsert: true });
  } else {
    memPrefs.set(userId, merged);
  }
  return merged;
}

export async function findUserByTelegramChatId(chatId: string): Promise<string | null> {
  const col = await getPrefsCol();
  if (col) {
    const prefs = await col.findOne({ 'telegram.chatId': chatId });
    return prefs?.userId || null;
  }
  for (const [userId, prefs] of memPrefs) {
    if (prefs.telegram.chatId === chatId) return userId;
  }
  return null;
}
