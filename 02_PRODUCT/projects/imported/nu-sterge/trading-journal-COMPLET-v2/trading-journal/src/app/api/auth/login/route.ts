import { NextRequest, NextResponse } from 'next/server';
import { cookies } from 'next/headers';
import { LoginSchema, toPublicUser } from '@/types/user';
import { verifyPassword } from '@/lib/auth/password';
import { signJWT, COOKIE_NAME, COOKIE_OPTIONS } from '@/lib/auth/jwt';
import { findUserByEmail, updateLastLogin } from '@/lib/db/users';

export const runtime = 'nodejs';

export async function POST(req: NextRequest) {
  try {
    const body = await req.json();
    const parsed = LoginSchema.safeParse(body);

    if (!parsed.success) {
      return NextResponse.json(
        { error: 'validation_failed', details: parsed.error.flatten().fieldErrors },
        { status: 400 }
      );
    }

    const { email, password } = parsed.data;

    // Find user (lowercase email is handled in findUserByEmail)
    const user = await findUserByEmail(email);

    // Always run password verification to avoid timing attacks
    // (even if user doesn't exist, we still do a hash comparison on a dummy)
    const DUMMY_HASH = '$2a$10$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy';
    const validPassword = user
      ? await verifyPassword(password, user.passwordHash)
      : await verifyPassword(password, DUMMY_HASH).then(() => false);

    if (!user || !validPassword) {
      return NextResponse.json(
        { error: 'invalid_credentials', message: 'Invalid email or password' },
        { status: 401 }
      );
    }

    // Update last login
    await updateLastLogin(user._id!);

    // Create JWT
    const token = await signJWT({
      sub: user._id!,
      email: user.email,
      plan: user.plan,
    });

    const cookieStore = await cookies();
    cookieStore.set(COOKIE_NAME, token, COOKIE_OPTIONS);

    return NextResponse.json({
      user: toPublicUser(user),
      message: 'Logged in successfully',
    });
  } catch (err: unknown) {
    const e = err as { message?: string };
    console.error('[Auth] Login error:', e);
    return NextResponse.json(
      { error: 'login_failed', details: e.message },
      { status: 500 }
    );
  }
}
