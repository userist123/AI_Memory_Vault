import { NextRequest, NextResponse } from 'next/server';
import { POLAR_WEBHOOK_SECRET } from '@/lib/billing/polar';
import { updateUserPlan } from '@/lib/db/users';
import { PLANS, type PlanId } from '@/lib/billing/plans';
import crypto from 'crypto';

export const runtime = 'nodejs';

/**
 * Map Polar product IDs back to our plan IDs
 */
function findPlanByProductId(productId: string): PlanId | null {
  for (const [planId, info] of Object.entries(PLANS)) {
    if (
      info.polarProductIds?.monthly === productId ||
      info.polarProductIds?.yearly === productId
    ) {
      return planId as PlanId;
    }
  }
  return null;
}

/**
 * Verify Polar webhook signature using HMAC-SHA256.
 * Polar signs webhooks with the webhook secret.
 */
function verifySignature(body: string, signature: string | null, secret: string): boolean {
  if (!signature || !secret) return false;

  try {
    const expected = crypto.createHmac('sha256', secret).update(body).digest('hex');
    // Strip any prefix like "sha256="
    const provided = signature.replace(/^sha256=/, '');

    // Constant-time comparison
    const expectedBuffer = Buffer.from(expected, 'hex');
    const providedBuffer = Buffer.from(provided, 'hex');

    if (expectedBuffer.length !== providedBuffer.length) return false;
    return crypto.timingSafeEqual(expectedBuffer, providedBuffer);
  } catch {
    return false;
  }
}

export async function POST(req: NextRequest) {
  try {
    const rawBody = await req.text();
    const signature =
      req.headers.get('polar-signature') ||
      req.headers.get('webhook-signature') ||
      req.headers.get('x-polar-signature');

    // Verify signature (skip in dev if no secret set)
    if (POLAR_WEBHOOK_SECRET) {
      if (!verifySignature(rawBody, signature, POLAR_WEBHOOK_SECRET)) {
        console.warn('[Webhook] Invalid signature');
        return NextResponse.json({ error: 'Invalid signature' }, { status: 401 });
      }
    } else if (process.env.NODE_ENV === 'production') {
      console.error('[Webhook] POLAR_WEBHOOK_SECRET not set in production!');
      return NextResponse.json({ error: 'Webhook secret not configured' }, { status: 500 });
    }

    const event = JSON.parse(rawBody);
    console.log('[Webhook] Received:', event.type);

    // Handle subscription events
    switch (event.type) {
      case 'subscription.created':
      case 'subscription.updated':
      case 'subscription.active': {
        const subscription = event.data;
        const userId = subscription.metadata?.userId;
        const productId = subscription.productId || subscription.product_id;

        if (!userId) {
          console.warn('[Webhook] No userId in subscription metadata');
          break;
        }

        const planId = findPlanByProductId(productId);
        if (!planId) {
          console.warn('[Webhook] Unknown productId:', productId);
          break;
        }

        await updateUserPlan(userId, planId);
        console.log(`[Webhook] Updated user ${userId} to plan ${planId}`);
        break;
      }

      case 'subscription.canceled':
      case 'subscription.revoked':
      case 'subscription.expired': {
        const subscription = event.data;
        const userId = subscription.metadata?.userId;

        if (!userId) break;

        // Downgrade to free
        await updateUserPlan(userId, 'free');
        console.log(`[Webhook] Downgraded user ${userId} to free`);
        break;
      }

      case 'order.created':
      case 'order.paid': {
        // Initial purchase - also triggers subscription.created for recurring
        const order = event.data;
        const userId = order.metadata?.userId;
        const productId = order.productId || order.product_id;

        if (!userId) break;

        const planId = findPlanByProductId(productId);
        if (planId) {
          await updateUserPlan(userId, planId);
          console.log(`[Webhook] Order paid: ${userId} → ${planId}`);
        }
        break;
      }

      default:
        console.log('[Webhook] Unhandled event type:', event.type);
    }

    return NextResponse.json({ received: true });
  } catch (err: unknown) {
    const e = err as { message?: string };
    console.error('[Webhook] Error:', e);
    return NextResponse.json(
      { error: 'Webhook processing failed', details: e.message },
      { status: 500 }
    );
  }
}
