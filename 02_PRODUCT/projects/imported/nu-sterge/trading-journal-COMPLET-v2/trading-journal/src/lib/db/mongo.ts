import { MongoClient, Db, Collection } from 'mongodb';
import type { JournalEntry } from '@/types/journal';
import type { Trade } from '@/types/trade';
import type { TradeReview, CoachReport } from '@/types/ai-review';

if (!process.env.MONGODB_URI) {
  console.warn('[MongoDB] MONGODB_URI not set - running in memory-only mode');
}

const uri = process.env.MONGODB_URI || '';
const dbName = process.env.MONGODB_DB || 'trading-journal';

let client: MongoClient | null = null;
let clientPromise: Promise<MongoClient> | null = null;

declare global {
  // eslint-disable-next-line no-var
  var _mongoClientPromise: Promise<MongoClient> | undefined;
}

if (uri) {
  if (process.env.NODE_ENV === 'development') {
    if (!global._mongoClientPromise) {
      client = new MongoClient(uri, {
        maxPoolSize: 10,
        serverSelectionTimeoutMS: 5000,
      });
      global._mongoClientPromise = client.connect();
    }
    clientPromise = global._mongoClientPromise;
  } else {
    client = new MongoClient(uri, {
      maxPoolSize: 10,
      serverSelectionTimeoutMS: 5000,
    });
    clientPromise = client.connect();
  }
}

export async function getDb(): Promise<Db | null> {
  if (!clientPromise) return null;
  const client = await clientPromise;
  return client.db(dbName);
}

// ===== Collections =====

export async function getJournalEntries(): Promise<Collection<JournalEntry> | null> {
  const db = await getDb();
  if (!db) return null;
  return db.collection<JournalEntry>('journal_entries');
}

export async function getTrades(): Promise<Collection<Trade> | null> {
  const db = await getDb();
  if (!db) return null;
  const col = db.collection<Trade>('trades');
  try {
    await col.createIndex({ userId: 1, externalId: 1 }, { unique: true, sparse: true });
    await col.createIndex({ userId: 1, entryTime: -1 });
    await col.createIndex({ userId: 1, broker: 1 });
    await col.createIndex({ userId: 1, symbol: 1 });
  } catch {}
  return col;
}

export async function getTradeReviews(): Promise<Collection<TradeReview> | null> {
  const db = await getDb();
  if (!db) return null;
  const col = db.collection<TradeReview>('trade_reviews');
  try {
    await col.createIndex({ userId: 1, tradeId: 1 }, { unique: true });
    await col.createIndex({ userId: 1, createdAt: -1 });
  } catch {}
  return col;
}

export async function getCoachReports(): Promise<Collection<CoachReport> | null> {
  const db = await getDb();
  if (!db) return null;
  const col = db.collection<CoachReport>('coach_reports');
  try {
    await col.createIndex({ userId: 1, createdAt: -1 });
    await col.createIndex({ userId: 1, periodEnd: -1 });
  } catch {}
  return col;
}

// ===== In-memory fallback =====
const memoryStore = {
  journalEntries: new Map<string, JournalEntry>(),
  trades: new Map<string, Trade>(),
  reviews: new Map<string, TradeReview>(),
  coachReports: new Map<string, CoachReport>(),
};

// ===== Journal =====
export async function saveJournalEntry(entry: JournalEntry): Promise<string> {
  const collection = await getJournalEntries();
  if (collection) {
    const result = await collection.insertOne(entry);
    return result.insertedId.toString();
  }
  const id = `mem_${Date.now()}_${Math.random().toString(36).slice(2, 9)}`;
  memoryStore.journalEntries.set(id, { ...entry, _id: id });
  return id;
}

export async function getJournalEntriesByUser(
  userId: string,
  limit: number = 50
): Promise<JournalEntry[]> {
  const collection = await getJournalEntries();
  if (collection) {
    return collection.find({ userId }).sort({ createdAt: -1 }).limit(limit).toArray();
  }
  return Array.from(memoryStore.journalEntries.values())
    .filter((e) => e.userId === userId)
    .sort((a, b) => b.createdAt.getTime() - a.createdAt.getTime())
    .slice(0, limit);
}

// ===== Trades =====
export async function saveTradesBatch(
  trades: Omit<Trade, '_id' | 'createdAt' | 'updatedAt'>[]
): Promise<{ inserted: number; duplicates: number; savedTrades: Trade[] }> {
  const collection = await getTrades();
  const now = new Date();
  let inserted = 0;
  let duplicates = 0;
  const savedTrades: Trade[] = [];

  if (collection) {
    for (const t of trades) {
      const fullTrade: Trade = { ...t, createdAt: now, updatedAt: now };

      if (fullTrade.externalId) {
        const existing = await collection.findOne({
          userId: fullTrade.userId,
          externalId: fullTrade.externalId,
        });
        if (existing) {
          duplicates++;
          continue;
        }
      }

      try {
        const result = await collection.insertOne(fullTrade);
        inserted++;
        savedTrades.push({ ...fullTrade, _id: result.insertedId.toString() });
      } catch (err: unknown) {
        const e = err as { code?: number };
        if (e.code === 11000) {
          duplicates++;
        } else {
          throw err;
        }
      }
    }
    return { inserted, duplicates, savedTrades };
  }

  for (const t of trades) {
    const fullTrade: Trade = { ...t, createdAt: now, updatedAt: now };

    if (fullTrade.externalId) {
      const dupe = Array.from(memoryStore.trades.values()).find(
        (existing) =>
          existing.userId === fullTrade.userId &&
          existing.externalId === fullTrade.externalId
      );
      if (dupe) {
        duplicates++;
        continue;
      }
    }

    const id = `mem_trade_${Date.now()}_${Math.random().toString(36).slice(2, 9)}`;
    const stored: Trade = { ...fullTrade, _id: id };
    memoryStore.trades.set(id, stored);
    savedTrades.push(stored);
    inserted++;
  }

  return { inserted, duplicates, savedTrades };
}

export async function getTradesByUser(
  userId: string,
  options: {
    limit?: number;
    offset?: number;
    symbol?: string;
    broker?: string;
    status?: string;
    since?: Date;
  } = {}
): Promise<Trade[]> {
  const { limit = 100, offset = 0, symbol, broker, status, since } = options;
  const collection = await getTrades();

  const filter: Record<string, unknown> = { userId };
  if (symbol) filter.symbol = symbol;
  if (broker) filter.broker = broker;
  if (status) filter.status = status;
  if (since) filter.entryTime = { $gte: since };

  if (collection) {
    return collection
      .find(filter)
      .sort({ entryTime: -1 })
      .skip(offset)
      .limit(limit)
      .toArray();
  }

  let trades = Array.from(memoryStore.trades.values()).filter((t) => t.userId === userId);
  if (symbol) trades = trades.filter((t) => t.symbol === symbol);
  if (broker) trades = trades.filter((t) => t.broker === broker);
  if (status) trades = trades.filter((t) => t.status === status);
  if (since) trades = trades.filter((t) => t.entryTime >= since);

  return trades
    .sort((a, b) => b.entryTime.getTime() - a.entryTime.getTime())
    .slice(offset, offset + limit);
}

export async function getTradeById(userId: string, tradeId: string): Promise<Trade | null> {
  const col = await getTrades();
  if (col) {
    try {
      const { ObjectId } = await import('mongodb');
      if (ObjectId.isValid(tradeId)) {
        const trade = await col.findOne({
          _id: new ObjectId(tradeId),
          userId,
        } as unknown as Record<string, unknown>);
        if (trade) return trade as Trade;
      }
      return col.findOne({ _id: tradeId, userId } as unknown as Record<string, unknown>);
    } catch {
      return null;
    }
  }
  return (
    Array.from(memoryStore.trades.values()).find(
      (t) => t._id === tradeId && t.userId === userId
    ) || null
  );
}

export async function getTradeStats(userId: string): Promise<{
  totalTrades: number;
  closedTrades: number;
  openTrades: number;
  totalPnL: number;
  winRate: number;
  avgWin: number;
  avgLoss: number;
  profitFactor: number;
}> {
  const trades = await getTradesByUser(userId, { limit: 10000 });
  const closed = trades.filter((t) => t.status === 'closed' && t.pnl !== null);
  const wins = closed.filter((t) => (t.pnl ?? 0) > 0);
  const losses = closed.filter((t) => (t.pnl ?? 0) < 0);

  const totalPnL = closed.reduce((s, t) => s + (t.pnl ?? 0), 0);
  const grossWin = wins.reduce((s, t) => s + (t.pnl ?? 0), 0);
  const grossLoss = Math.abs(losses.reduce((s, t) => s + (t.pnl ?? 0), 0));

  return {
    totalTrades: trades.length,
    closedTrades: closed.length,
    openTrades: trades.filter((t) => t.status === 'open').length,
    totalPnL,
    winRate: closed.length > 0 ? (wins.length / closed.length) * 100 : 0,
    avgWin: wins.length > 0 ? grossWin / wins.length : 0,
    avgLoss: losses.length > 0 ? -grossLoss / losses.length : 0,
    profitFactor: grossLoss > 0 ? grossWin / grossLoss : wins.length > 0 ? Infinity : 0,
  };
}

// ===== AI Reviews =====
export async function saveTradeReview(review: TradeReview): Promise<string> {
  const col = await getTradeReviews();
  if (col) {
    const result = await col.findOneAndUpdate(
      { userId: review.userId, tradeId: review.tradeId },
      { $set: review },
      { upsert: true, returnDocument: 'after' }
    );
    return result?._id?.toString() || '';
  }
  const key = `${review.userId}:${review.tradeId}`;
  memoryStore.reviews.set(key, review);
  return key;
}

export async function getTradeReview(
  userId: string,
  tradeId: string
): Promise<TradeReview | null> {
  const col = await getTradeReviews();
  if (col) {
    return col.findOne({ userId, tradeId });
  }
  return memoryStore.reviews.get(`${userId}:${tradeId}`) || null;
}

// ===== Coach Reports =====
export async function saveCoachReport(report: CoachReport): Promise<string> {
  const col = await getCoachReports();
  if (col) {
    const result = await col.insertOne(report);
    return result.insertedId.toString();
  }
  const id = `coach_${Date.now()}_${Math.random().toString(36).slice(2, 9)}`;
  memoryStore.coachReports.set(id, { ...report, _id: id });
  return id;
}

export async function getLatestCoachReport(userId: string): Promise<CoachReport | null> {
  const col = await getCoachReports();
  if (col) {
    return col.findOne({ userId }, { sort: { createdAt: -1 } });
  }
  const reports = Array.from(memoryStore.coachReports.values())
    .filter((r) => r.userId === userId)
    .sort((a, b) => b.createdAt.getTime() - a.createdAt.getTime());
  return reports[0] || null;
}
