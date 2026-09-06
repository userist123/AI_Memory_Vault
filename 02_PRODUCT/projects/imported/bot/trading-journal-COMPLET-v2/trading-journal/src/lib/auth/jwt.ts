import { SignJWT, jwtVerify } from 'jose';
import type { JWTPayload } from '@/types/user';

const JWT_EXPIRES_IN = process.env.JWT_EXPIRES_IN || '7d';
const JWT_ALG = 'HS256';

function getSecret(): Uint8Array {
  const secret = process.env.JWT_SECRET;
  if (!secret) {
    // Dev fallback — works but warns
    if (process.env.NODE_ENV !== 'production') {
      console.warn(
        '[Auth] JWT_SECRET not set. Using dev fallback. Set JWT_SECRET in production!'
      );
      return new TextEncoder().encode(
        'dev-only-secret-please-set-JWT_SECRET-in-production-min-32-chars'
      );
    }
    throw new Error('JWT_SECRET environment variable is required in production');
  }
  if (secret.length < 32) {
    throw new Error('JWT_SECRET must be at least 32 characters');
  }
  return new TextEncoder().encode(secret);
}

export async function signJWT(payload: Omit<JWTPayload, 'iat' | 'exp'>): Promise<string> {
  return new SignJWT({ ...payload })
    .setProtectedHeader({ alg: JWT_ALG })
    .setIssuedAt()
    .setExpirationTime(JWT_EXPIRES_IN)
    .setSubject(payload.sub)
    .sign(getSecret());
}

export async function verifyJWT(token: string): Promise<JWTPayload | null> {
  try {
    const { payload } = await jwtVerify(token, getSecret(), {
      algorithms: [JWT_ALG],
    });
    return payload as unknown as JWTPayload;
  } catch (err) {
    const e = err as { code?: string; message?: string };
    if (e.code !== 'ERR_JWT_EXPIRED') {
      console.warn('[Auth] JWT verify failed:', e.message);
    }
    return null;
  }
}

export const COOKIE_NAME = 'tj_session';

export const COOKIE_OPTIONS = {
  httpOnly: true,
  secure: process.env.NODE_ENV === 'production',
  sameSite: 'lax' as const,
  path: '/',
  maxAge: 60 * 60 * 24 * 7, // 7 days
};
