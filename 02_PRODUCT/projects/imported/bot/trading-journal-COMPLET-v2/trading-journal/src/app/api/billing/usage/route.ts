import { NextResponse } from 'next/server';
import { getCurrentUser } from '@/lib/auth/session';
import { getAllUsage } from '@/lib/db/usage';
import { getPlan } from '@/lib/billing/plans';

export const runtime = 'nodejs';

export async function GET() {
  const user = await getCurrentUser();
  if (!user) {
    return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  }

  const usage = await getAllUsage(user._id!);
  const plan = getPlan(user.plan);

  return NextResponse.json({
    plan: user.plan,
    limits: plan.limits,
    usage: {
      daily: usage.daily,
      monthly: usage.monthly,
    },
  });
}
