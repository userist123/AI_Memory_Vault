import { NextRequest, NextResponse } from 'next/server';
import { z } from 'zod';
import { getCurrentUser } from '@/lib/auth/session';
import { getNotificationPrefs, updateNotificationPrefs } from '@/lib/db/alerts';

export const runtime = 'nodejs';

const UpdateSchema = z.object({
  email: z.object({
    enabled: z.boolean(),
    address: z.string().email().optional(),
  }).optional(),
  telegram: z.object({
    enabled: z.boolean(),
    chatId: z.string().optional(),
  }).optional(),
  inApp: z.object({
    enabled: z.boolean(),
  }).optional(),
  filters: z.object({
    minStrength: z.number().min(0).max(100).optional(),
    minRiskReward: z.number().min(0).max(10).optional(),
    symbols: z.array(z.string()).optional(),
    signalTypes: z.array(z.string()).optional(),
  }).optional(),
});

export async function GET() {
  const user = await getCurrentUser();
  if (!user) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const prefs = await getNotificationPrefs(user._id!);
  return NextResponse.json({ prefs });
}

export async function PATCH(req: NextRequest) {
  const user = await getCurrentUser();
  if (!user) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });

  const body = await req.json();
  const parsed = UpdateSchema.safeParse(body);
  if (!parsed.success) {
    return NextResponse.json({ error: 'Invalid', details: parsed.error.errors }, { status: 400 });
  }

  const updated = await updateNotificationPrefs(user._id!, parsed.data as Parameters<typeof updateNotificationPrefs>[1]);
  return NextResponse.json({ prefs: updated });
}
