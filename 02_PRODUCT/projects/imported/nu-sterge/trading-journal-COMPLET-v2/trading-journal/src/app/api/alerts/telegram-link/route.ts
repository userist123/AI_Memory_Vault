import { NextResponse } from 'next/server';
import { SignJWT } from 'jose';
import { getCurrentUser } from '@/lib/auth/session';

export const runtime = 'nodejs';

function getSecret(): Uint8Array {
  const secret = process.env.JWT_SECRET;
  if (!secret) {
    return new TextEncoder().encode(
      'dev-only-secret-please-set-JWT_SECRET-in-production-min-32-chars'
    );
  }
  return new TextEncoder().encode(secret);
}

export async function POST() {
  const user = await getCurrentUser();
  if (!user) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });

  // Short-lived token - 15 min
  const token = await new SignJWT({ sub: user._id!, email: user.email, plan: user.plan })
    .setProtectedHeader({ alg: 'HS256' })
    .setIssuedAt()
    .setExpirationTime('15m')
    .setSubject(user._id!)
    .sign(getSecret());

  const botUsername = process.env.TELEGRAM_BOT_USERNAME || 'your_bot';
  const deepLink = `https://t.me/${botUsername}?start=${token}`;

  return NextResponse.json({ token, deepLink, botUsername, expiresInMinutes: 15 });
}
