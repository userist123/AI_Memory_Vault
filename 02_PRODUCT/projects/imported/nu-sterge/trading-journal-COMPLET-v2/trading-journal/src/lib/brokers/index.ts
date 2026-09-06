import { Collection } from 'mongodb';
import { getDb } from '@/lib/db/mongo';
import type { BrokerAdapter, BrokerCredentials, BrokerId } from './types';
import { binanceAdapter } from './binance';
import { alpacaAdapter } from './alpaca';
import { ibkrAdapter } from './ibkr';
import { encryptString, decryptString } from './crypto';

export const adapters: Record<BrokerId, BrokerAdapter> = {
  binance: binanceAdapter,
  alpaca: alpacaAdapter,
  ibkr: ibkrAdapter,
};

export function getAdapter(brokerId: BrokerId): BrokerAdapter {
  return adapters[brokerId];
}

// ===== DB ops =====
const memCreds = new Map<string, BrokerCredentials>();

async function getCredsCol(): Promise<Collection<BrokerCredentials> | null> {
  const db = await getDb();
  if (!db) return null;
  const col = db.collection<BrokerCredentials>('broker_credentials');
  try {
    await col.createIndex({ userId: 1, brokerId: 1, testnet: 1 }, { unique: true });
  } catch {}
  return col;
}

export async function saveBrokerCredentials(params: {
  userId: string;
  brokerId: BrokerId;
  apiKey: string;
  apiSecret: string;
  testnet: boolean;
  label?: string;
  permissions: string[];
  extras?: Record<string, string>;
}): Promise<void> {
  const encrypted = {
    brokerId: params.brokerId,
    userId: params.userId,
    encryptedApiKey: await encryptString(params.apiKey),
    encryptedApiSecret: await encryptString(params.apiSecret),
    testnet: params.testnet,
    label: params.label,
    permissions: params.permissions,
    createdAt: new Date(),
    encryptedExtras: params.extras
      ? Object.fromEntries(
          await Promise.all(Object.entries(params.extras).map(async ([k, v]) => [k, await encryptString(v)]))
        )
      : undefined,
  };

  const col = await getCredsCol();
  if (col) {
    await col.findOneAndUpdate(
      { userId: params.userId, brokerId: params.brokerId, testnet: params.testnet },
      { $set: encrypted },
      { upsert: true }
    );
    return;
  }
  memCreds.set(`${params.userId}:${params.brokerId}:${params.testnet}`, encrypted);
}

export async function getBrokerCredentials(
  userId: string,
  brokerId: BrokerId,
  testnet: boolean
): Promise<{ apiKey: string; apiSecret: string; testnet: boolean; extras?: Record<string, string> } | null> {
  const col = await getCredsCol();
  let record: BrokerCredentials | null = null;

  if (col) {
    record = await col.findOne({ userId, brokerId, testnet });
  } else {
    record = memCreds.get(`${userId}:${brokerId}:${testnet}`) || null;
  }

  if (!record) return null;

  const extras: Record<string, string> = {};
  if (record.encryptedExtras) {
    for (const [k, v] of Object.entries(record.encryptedExtras)) {
      extras[k] = await decryptString(v);
    }
  }

  return {
    apiKey: await decryptString(record.encryptedApiKey),
    apiSecret: await decryptString(record.encryptedApiSecret),
    testnet: record.testnet,
    extras: record.encryptedExtras ? extras : undefined,
  };
}

export async function listUserBrokers(userId: string): Promise<Array<{
  brokerId: BrokerId;
  testnet: boolean;
  label?: string;
  permissions: string[];
  createdAt: Date;
  lastUsedAt?: Date;
}>> {
  const col = await getCredsCol();
  const records: BrokerCredentials[] = col
    ? await col.find({ userId }).toArray()
    : Array.from(memCreds.values()).filter((c) => c.userId === userId);

  return records.map((r) => ({
    brokerId: r.brokerId,
    testnet: r.testnet,
    label: r.label,
    permissions: r.permissions,
    createdAt: r.createdAt,
    lastUsedAt: r.lastUsedAt,
  }));
}

export async function deleteBrokerCredentials(
  userId: string,
  brokerId: BrokerId,
  testnet: boolean
): Promise<boolean> {
  const col = await getCredsCol();
  if (col) {
    const result = await col.deleteOne({ userId, brokerId, testnet });
    return result.deletedCount > 0;
  }
  return memCreds.delete(`${userId}:${brokerId}:${testnet}`);
}
