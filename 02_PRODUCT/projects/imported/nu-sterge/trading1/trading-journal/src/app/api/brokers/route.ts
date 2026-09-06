import { NextRequest, NextResponse } from 'next/server';
import { z } from 'zod';
import { getCurrentUser } from '@/lib/auth/session';
import {
  getAdapter,
  saveBrokerCredentials,
  listUserBrokers,
  deleteBrokerCredentials,
} from '@/lib/brokers';
import { BrokerIdSchema } from '@/lib/brokers/types';

export const runtime = 'nodejs';

const ConnectRequestSchema = z.object({
  brokerId: BrokerIdSchema,
  apiKey: z.string().min(1),
  apiSecret: z.string().min(1),
  testnet: z.boolean().default(true),
  label: z.string().optional(),
});

export async function GET() {
  const user = await getCurrentUser();
  if (!user) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const brokers = await listUserBrokers(user._id!);
  return NextResponse.json({ brokers });
}

export async function POST(req: NextRequest) {
  try {
    const user = await getCurrentUser();
    if (!user) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });

    const body = await req.json();
    const parsed = ConnectRequestSchema.safeParse(body);
    if (!parsed.success) {
      return NextResponse.json(
        { error: 'Invalid request', details: parsed.error.errors },
        { status: 400 }
      );
    }

    const { brokerId, apiKey, apiSecret, testnet, label } = parsed.data;

    // Validate credentials FIRST (don't save bad keys)
    const adapter = getAdapter(brokerId);
    const validation = await adapter.validateCredentials({
      apiKey,
      apiSecret,
      testnet,
    });

    if (!validation.valid) {
      return NextResponse.json(
        {
          error: 'validation_failed',
          message: validation.error || 'Cheile nu sunt valide. Verifică în dashboard-ul brokerului.',
        },
        { status: 400 }
      );
    }

    await saveBrokerCredentials({
      userId: user._id!,
      brokerId,
      apiKey,
      apiSecret,
      testnet,
      label,
      permissions: validation.permissions,
    });

    return NextResponse.json({
      success: true,
      broker: {
        brokerId,
        testnet,
        label,
        permissions: validation.permissions,
        accountType: validation.accountType,
      },
    });
  } catch (err: unknown) {
    const e = err as { message?: string };
    console.error('[Brokers] Connect error:', e);
    return NextResponse.json(
      { error: 'Connect failed', details: e.message },
      { status: 500 }
    );
  }
}

export async function DELETE(req: NextRequest) {
  try {
    const user = await getCurrentUser();
    if (!user) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });

    const { searchParams } = new URL(req.url);
    const brokerId = searchParams.get('brokerId');
    const testnet = searchParams.get('testnet') === 'true';

    if (!brokerId) {
      return NextResponse.json({ error: 'brokerId required' }, { status: 400 });
    }

    const parsed = BrokerIdSchema.safeParse(brokerId);
    if (!parsed.success) {
      return NextResponse.json({ error: 'Invalid brokerId' }, { status: 400 });
    }

    const deleted = await deleteBrokerCredentials(user._id!, parsed.data, testnet);
    return NextResponse.json({ success: deleted });
  } catch (err: unknown) {
    const e = err as { message?: string };
    return NextResponse.json({ error: 'Delete failed', details: e.message }, { status: 500 });
  }
}
