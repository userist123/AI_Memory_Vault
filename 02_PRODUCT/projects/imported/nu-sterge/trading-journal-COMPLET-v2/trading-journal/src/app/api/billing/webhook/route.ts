import { NextRequest, NextResponse } from 'next/server';
import { POLAR_WEBHOOK_SECRET } from '@/lib/billing/polar';
import { updateUserPlan } from '@/lib/db/users';
import { PLANS, type PlanId } from '@/lib/billing/plans';
import { validateEvent, WebhookVerificationError } from '@polar-sh/sdk/webhooks';
import { SDKValidationError } from '@polar-sh/sdk/models/errors/sdkvalidationerror.js';

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

type WebhookEvent = { type: string; data: Record<string, unknown> };

/**
 * Verify and parse a Polar webhook.
 * Polar signs with Standard Webhooks (webhook-id / webhook-timestamp /
 * webhook-signature headers), which the SDK's validateEvent checks.
 */
function readEvent(rawBody: string, req: NextRequest): WebhookEvent {
  try {
    return validateEvent(
      rawBody,
      Object.fromEntries(req.headers.entries()),
      POLAR_WEBHOOK_SECRET
    ) as unknown as WebhookEvent;
  } catch (err) {
    // Signature is checked before parsing, so a parse error means a verified
    // event the SDK has no schema for (e.g. a newer event type).
    if (err instanceof SDKValidationError) return JSON.parse(rawBody);
    throw err;
  }
}

// Verified events arrive camelCase from the SDK; unverified dev events are raw snake_case.
function field(data: Record<string, unknown>, camel: string, snake: string): string | undefined {
  return (data[camel] ?? data[snake]) as string | undefined;
}

export async function POST(req: NextRequest) {
  try {
    const rawBody = await req.text();

    let event: WebhookEvent;
    if (POLAR_WEBHOOK_SECRET) {
      try {
        event = readEvent(rawBody, req);
      } catch (err) {
        if (err instanceof WebhookVerificationError) {
          console.warn('[Webhook] Invalid signature');
          return NextResponse.json({ error: 'Invalid signature' }, { status: 403 });
        }
        throw err;
      }
    } else if (process.env.NODE_ENV === 'production') {
      console.error('[Webhook] POLAR_WEBHOOK_SECRET not set in production!');
      return NextResponse.json({ error: 'Webhook secret not configured' }, { status: 500 });
    } else {
      // Dev only: accept unsigned events when no secret is set
      event = JSON.parse(rawBody);
    }

    console.log('[Webhook] Received:', event.type);
    const data = event.data;
    const userId = (data.metadata as Record<string, unknown> | undefined)?.userId as
      | string
      | undefined;

    switch (event.type) {
      case 'subscription.created':
      case 'subscription.updated':
      case 'subscription.active':
      case 'subscription.uncanceled': {
        if (!userId) {
          console.warn('[Webhook] No userId in subscription metadata');
          break;
        }
        // Only grant access for live subscriptions; `updated` also fires for
        // incomplete, past_due and ended ones.
        const status = data.status as string | undefined;
        if (status !== 'active' && status !== 'trialing') {
          console.log(`[Webhook] Subscription status ${status}, plan unchanged`);
          break;
        }

        const productId = field(data, 'productId', 'product_id');
        const planId = productId ? findPlanByProductId(productId) : null;
        if (!planId) {
          console.warn('[Webhook] Unknown productId:', productId);
          break;
        }

        await updateUserPlan(userId, planId);
        console.log(`[Webhook] Updated user ${userId} to plan ${planId}`);
        break;
      }

      case 'subscription.canceled': {
        // Cancellation takes effect at the end of the paid period; access is
        // removed on subscription.revoked.
        console.log(`[Webhook] Subscription canceled for ${userId}, access kept until revoked`);
        break;
      }

      case 'subscription.revoked': {
        if (!userId) break;

        await updateUserPlan(userId, 'free');
        console.log(`[Webhook] Downgraded user ${userId} to free`);
        break;
      }

      case 'order.paid': {
        // order.created fires before payment, so only paid orders grant a plan
        if (!userId) break;

        const productId = field(data, 'productId', 'product_id');
        const planId = productId ? findPlanByProductId(productId) : null;
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
