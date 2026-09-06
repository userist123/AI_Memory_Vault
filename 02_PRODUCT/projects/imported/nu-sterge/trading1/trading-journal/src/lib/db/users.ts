import { Collection, ObjectId } from 'mongodb';
import { getDb } from './mongo';
import type { User } from '@/types/user';

// In-memory fallback for dev without Mongo
const memUsers = new Map<string, User>();
const memUsersByEmail = new Map<string, string>(); // email -> id

export async function getUsers(): Promise<Collection<User> | null> {
  const db = await getDb();
  if (!db) return null;
  const col = db.collection<User>('users');
  try {
    await col.createIndex({ email: 1 }, { unique: true });
    await col.createIndex({ createdAt: -1 });
  } catch {}
  return col;
}

export async function findUserByEmail(email: string): Promise<User | null> {
  const normalizedEmail = email.toLowerCase().trim();
  const col = await getUsers();

  if (col) {
    return col.findOne({ email: normalizedEmail });
  }

  const id = memUsersByEmail.get(normalizedEmail);
  if (!id) return null;
  return memUsers.get(id) || null;
}

export async function getUserById(userId: string): Promise<User | null> {
  const col = await getUsers();

  if (col) {
    try {
      if (ObjectId.isValid(userId)) {
        const user = await col.findOne({
          _id: new ObjectId(userId),
        } as unknown as Record<string, unknown>);
        if (user) return user as User;
      }
      return col.findOne({ _id: userId } as unknown as Record<string, unknown>);
    } catch {
      return null;
    }
  }

  return memUsers.get(userId) || null;
}

export async function createUser(
  userData: Omit<User, '_id' | 'createdAt' | 'updatedAt'>
): Promise<User> {
  const normalizedEmail = userData.email.toLowerCase().trim();
  const now = new Date();

  const col = await getUsers();

  if (col) {
    const existing = await col.findOne({ email: normalizedEmail });
    if (existing) {
      throw new Error('EMAIL_EXISTS');
    }

    const user: User = {
      ...userData,
      email: normalizedEmail,
      createdAt: now,
      updatedAt: now,
    };

    try {
      const result = await col.insertOne(user);
      return { ...user, _id: result.insertedId.toString() };
    } catch (err: unknown) {
      const e = err as { code?: number };
      if (e.code === 11000) {
        throw new Error('EMAIL_EXISTS');
      }
      throw err;
    }
  }

  // In-memory
  if (memUsersByEmail.has(normalizedEmail)) {
    throw new Error('EMAIL_EXISTS');
  }
  const id = `mem_user_${Date.now()}_${Math.random().toString(36).slice(2, 9)}`;
  const user: User = {
    ...userData,
    _id: id,
    email: normalizedEmail,
    createdAt: now,
    updatedAt: now,
  };
  memUsers.set(id, user);
  memUsersByEmail.set(normalizedEmail, id);
  return user;
}

export async function updateLastLogin(userId: string): Promise<void> {
  const now = new Date();
  const col = await getUsers();

  if (col) {
    try {
      if (ObjectId.isValid(userId)) {
        await col.updateOne(
          { _id: new ObjectId(userId) } as unknown as Record<string, unknown>,
          { $set: { lastLoginAt: now, updatedAt: now } }
        );
      } else {
        await col.updateOne(
          { _id: userId } as unknown as Record<string, unknown>,
          { $set: { lastLoginAt: now, updatedAt: now } }
        );
      }
    } catch {}
    return;
  }

  const user = memUsers.get(userId);
  if (user) {
    memUsers.set(userId, { ...user, lastLoginAt: now, updatedAt: now });
  }
}

export async function updateUserPlan(
  userId: string,
  plan: 'free' | 'pro' | 'elite'
): Promise<void> {
  const now = new Date();
  const col = await getUsers();

  if (col) {
    try {
      if (ObjectId.isValid(userId)) {
        await col.updateOne(
          { _id: new ObjectId(userId) } as unknown as Record<string, unknown>,
          { $set: { plan, updatedAt: now } }
        );
      }
    } catch {}
    return;
  }

  const user = memUsers.get(userId);
  if (user) {
    memUsers.set(userId, { ...user, plan, updatedAt: now });
  }
}
