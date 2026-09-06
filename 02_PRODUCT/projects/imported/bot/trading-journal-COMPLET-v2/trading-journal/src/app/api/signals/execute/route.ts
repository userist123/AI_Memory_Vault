import { NextRequest, NextResponse } from 'next/server';
import { z } from 'zod';
import { getCurrentUser } from '@/lib/auth/session';
import { getAlert, updateAlertStatus } from '@/lib/db/alerts';
import { executeSignal } from '@/lib/signals/execution';
import { BrokerIdSchema } from '@/lib/brokers/types';

export const runtime = 'nodejs';
export const maxDuration = 30;

const ExecuteRequestSchema = z.object({
  signalId: z.string().min(1),
  brokerId: BrokerIdSchema,
  testnet: z.boolean().default(true),
  reason: z.string().min(3, 'Motivul trebuie să aibă min 3 caractere'),
  riskPercentOverride: z.number().min(0.1).max(5).optional(),
});

export async function POST(req: NextRequest) {
  try {
    const user = await getCurrentUser();
    if (!user) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });

    const body = await req.json();
    const parsed = ExecuteRequestSchema.safeParse(body);
    if (!parsed.success) {
      return NextResponse.json(
        { error: 'Invalid request', details: parsed.error.errors },
        { status: 400 }
      );
    }

    const alert = await getAlert(parsed.data.signalId);
    if (!alert) {
      return NextResponse.json({ error: 'Alert not found' }, { status: 404 });
    }

    if (alert.userId !== user._id) {
      return NextResponse.json({ error: 'Not your alert' }, { status: 403 });
    }

    if (alert.status !== 'pending') {
      return NextResponse.json(
        { error: `Alert already ${alert.status}` },
        { status: 409 }
      );
    }

    const result = await executeSignal({
      userId: user._id!,
      signalId: parsed.data.signalId,
      signal: alert.signal,
      brokerId: parsed.data.brokerId,
      testnet: parsed.data.testnet,
      riskPercentOverride: parsed.data.riskPercentOverride,
      reason: parsed.data.reason,
    });

    if (!result.success) {
      return NextResponse.json(
        {
          error: result.blockedByRisk ? 'blocked_by_risk' : 'execution_failed',
          message: result.error,
          warnings: result.warnings,
        },
        { status: result.blockedByRisk ? 403 : 500 }
      );
    }

    return NextResponse.json(result);
  } catch (err: unknown) {
    const e = err as { message?: string };
    console.error('[Execute] Error:', e);
    return NextResponse.json({ error: 'Execute failed', details: e.message }, { status: 500 });
  }
}

// Skip an alert
export async function DELETE(req: NextRequest) {
  const user = await getCurrentUser();
  if (!user) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });

  const { searchParams } = new URL(req.url);
  const signalId = searchParams.get('signalId');
  const reason = searchParams.get('reason') || '';

  if (!signalId) return NextResponse.json({ error: 'signalId required' }, { status: 400 });

  const alert = await getAlert(signalId);
  if (!alert || alert.userId !== user._id) {
    return NextResponse.json({ error: 'Not found' }, { status: 404 });
  }

  await updateAlertStatus(signalId, 'skipped', { skipReason: reason });
  return NextResponse.json({ success: true });
}
