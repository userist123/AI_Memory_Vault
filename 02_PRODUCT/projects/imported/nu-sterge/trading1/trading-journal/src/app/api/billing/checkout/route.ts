import { NextRequest, NextResponse } from 'next/server';
import { z } from 'zod';
import { polar, isPolarConfigured } from '@/lib/billing/polar';
import { getCurrentUser } from '@/lib/auth/session';
import { PLANS, type PlanId } from '@/lib/billing/plans';

export const runtime = 'nodejs';

const CheckoutRequestSchema = z.object({
  plan: z.enum(['pro', 'elite']),
  period: z.enum(['monthly', 'yearly']),
});

export async function POST(req: NextRequest) {
  try {
    const user = await getCurrentUser();
    if (!user) {
      return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
    }

    const body = await req.json();
    const parsed = CheckoutRequestSchema.safeParse(body);
    if (!parsed.success) {
      return NextResponse.json(
        { error: 'Invalid request', details: parsed.error.errors },
        { status: 400 }
      );
    }

    const { plan, period } = parsed.data;
    const planInfo = PLANS[plan as PlanId];
    const productId = planInfo.polarProductIds?.[period];

    if (!isPolarConfigured() || !polar || !productId) {
      // Graceful degradation: return an informative response
      return NextResponse.json(
        {
          error: 'billing_not_configured',
          message:
            'Payment processor not configured yet. Set POLAR_ACCESS_TOKEN and product IDs in .env.local',
          hint: 'For now, plan upgrades can be done manually via /api/billing/admin-upgrade (dev only)',
        },
        { status: 503 }
      );
    }

    const appUrl = process.env.NEXT_PUBLIC_APP_URL || 'http://localhost:3000';

    // Create checkout session with Polar
    const checkout = await polar.checkouts.create({
      productId,
      successUrl: `${appUrl}/dashboard?upgrade=success`,
      customerEmail: user.email,
      customerName: user.name || undefined,
      metadata: {
        userId: user._id!,
        plan,
        period,
      },
    });

    return NextResponse.json({
      url: checkout.url,
      sessionId: checkout.id,
    });
  } catch (err: unknown) {
    const e = err as { message?: string };
    console.error('[Checkout] Error:', e);
    return NextResponse.json(
      { error: 'Checkout failed', details: e.message },
      { status: 500 }
    );
  }
}

/**
 * DEV-ONLY: manual upgrade without going through Polar.
 * Useful for testing before billing is configured.
 */
export async function PATCH(req: NextRequest) {
  if (process.env.NODE_ENV === 'production') {
    return NextResponse.json({ error: 'Not available in production' }, { status: 403 });
  }

  const user = await getCurrentUser();
  if (!user) {
    return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  }

  const { plan } = await req.json();
  if (!['free', 'pro', 'elite'].includes(plan)) {
    return NextResponse.json({ error: 'Invalid plan' }, { status: 400 });
  }

  const { updateUserPlan } = await import('@/lib/db/users');
  await updateUserPlan(user._id!, plan);

  return NextResponse.json({
    success: true,
    plan,
    note: 'DEV mode: plan updated without payment. In production, use /api/billing/checkout.',
  });
}
