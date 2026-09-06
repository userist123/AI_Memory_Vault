import { NextRequest, NextResponse } from 'next/server';
import { cookies } from 'next/headers';
import { SignupSchema, toPublicUser } from '@/types/user';
import { hashPassword } from '@/lib/auth/password';
import { signJWT, COOKIE_NAME, COOKIE_OPTIONS } from '@/lib/auth/jwt';
import { createUser } from '@/lib/db/users';

export const runtime = 'nodejs';

export async function POST(req: NextRequest) {
  try {
    const body = await req.json();
    const parsed = SignupSchema.safeParse(body);

    if (!parsed.success) {
      return NextResponse.json(
        {
          error: 'validation_failed',
          details: parsed.error.flatten().fieldErrors,
        },
        { status: 400 }
      );
    }

    const { email, password, name, language } = parsed.data;

    // Hash password
    const passwordHash = await hashPassword(password);

    // Create user
    let user;
    try {
      user = await createUser({
        email,
        passwordHash,
        name: name || null,
        language,
        plan: 'free',
        emailVerified: false,
        lastLoginAt: null,
      });
    } catch (err: unknown) {
      const e = err as { message?: string };
      if (e.message === 'EMAIL_EXISTS') {
        return NextResponse.json(
          { error: 'email_exists', message: 'An account with this email already exists' },
          { status: 409 }
        );
      }
      throw err;
    }

    // Create JWT
    const token = await signJWT({
      sub: user._id!,
      email: user.email,
      plan: user.plan,
    });

    // Set cookie
    const cookieStore = await cookies();
    cookieStore.set(COOKIE_NAME, token, COOKIE_OPTIONS);

    return NextResponse.json({
      user: toPublicUser(user),
      message: 'Account created successfully',
    });
  } catch (err: unknown) {
    const e = err as { message?: string };
    console.error('[Auth] Signup error:', e);
    return NextResponse.json(
      { error: 'signup_failed', details: e.message },
      { status: 500 }
    );
  }
}
