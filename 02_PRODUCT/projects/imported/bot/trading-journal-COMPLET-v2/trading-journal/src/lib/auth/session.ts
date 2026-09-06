import { cookies } from 'next/headers';
import { NextRequest } from 'next/server';
import { verifyJWT, COOKIE_NAME } from './jwt';
import { getUserById } from '@/lib/db/users';
import type { PublicUser } from '@/types/user';
import { toPublicUser } from '@/types/user';

/**
 * Get current user from cookies in Server Components / API routes.
 * Returns null if not authenticated.
 */
export async function getCurrentUser(): Promise<PublicUser | null> {
  try {
    const cookieStore = await cookies();
    const token = cookieStore.get(COOKIE_NAME)?.value;
    if (!token) return null;

    const payload = await verifyJWT(token);
    if (!payload?.sub) return null;

    const user = await getUserById(payload.sub);
    if (!user) return null;

    return toPublicUser(user);
  } catch (err) {
    console.warn('[Auth] getCurrentUser failed:', err);
    return null;
  }
}

/**
 * Get user ID from request (for API routes).
 * Returns null if not authenticated.
 */
export async function getUserIdFromRequest(req?: NextRequest): Promise<string | null> {
  try {
    let token: string | undefined;

    if (req) {
      token = req.cookies.get(COOKIE_NAME)?.value;
    } else {
      const cookieStore = await cookies();
      token = cookieStore.get(COOKIE_NAME)?.value;
    }

    if (!token) return null;

    const payload = await verifyJWT(token);
    return payload?.sub || null;
  } catch {
    return null;
  }
}

/**
 * Require authentication - throws or returns user ID.
 * Use in API routes that need auth.
 */
export async function requireUserId(req?: NextRequest): Promise<string> {
  const userId = await getUserIdFromRequest(req);
  if (!userId) {
    throw new AuthError('Unauthorized', 401);
  }
  return userId;
}

export class AuthError extends Error {
  constructor(message: string, public status: number = 401) {
    super(message);
    this.name = 'AuthError';
  }
}
