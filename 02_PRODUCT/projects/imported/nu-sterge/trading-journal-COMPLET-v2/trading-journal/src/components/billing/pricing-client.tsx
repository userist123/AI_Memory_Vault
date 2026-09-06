'use client';

import { useState, useEffect } from 'react';
import { useLocale, useTranslations } from 'next-intl';
import { useRouter } from '@/lib/i18n/routing';
import { Button } from '@/components/ui/button';
import { Check, X, Zap, Crown, Loader2, Sparkles } from 'lucide-react';
import { cn } from '@/lib/utils';
import { PLANS, type PlanId } from '@/lib/billing/plans';

interface PricingClientProps {
  currentPlan?: PlanId;
  isDev?: boolean;
}

export function PricingClient({ currentPlan = 'free', isDev = false }: PricingClientProps) {
  const locale = useLocale() as 'ro' | 'en';
  const t = useTranslations('pricing');
  const router = useRouter();
  const [billingPeriod, setBillingPeriod] = useState<'monthly' | 'yearly'>('monthly');
  const [loading, setLoading] = useState<PlanId | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [userUsage, setUserUsage] = useState<{ plan: PlanId; daily: Record<string, number>; monthly: Record<string, number> } | null>(null);

  useEffect(() => {
    // Load current usage
    fetch('/api/billing/usage')
      .then((r) => (r.ok ? r.json() : null))
      .then((d) => {
        if (d) {
          setUserUsage({ plan: d.plan, daily: d.usage.daily, monthly: d.usage.monthly });
        }
      })
      .catch(() => {});
  }, []);

  const handleCheckout = async (plan: PlanId) => {
    if (plan === 'free') return;
    setError(null);
    setLoading(plan);

    try {
      const res = await fetch('/api/billing/checkout', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ plan, period: billingPeriod }),
      });

      const data = await res.json();

      if (res.ok && data.url) {
        window.location.href = data.url;
        return;
      }

      if (data.error === 'billing_not_configured' && isDev) {
        // Dev-only manual upgrade
        const manualRes = await fetch('/api/billing/checkout', {
          method: 'PATCH',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ plan }),
        });
        if (manualRes.ok) {
          router.refresh();
          setError(
            locale === 'ro'
              ? `✓ Upgrade DEV: acum ești pe ${plan.toUpperCase()}. În producție, se procesează via Polar.`
              : `✓ DEV upgrade: now on ${plan.toUpperCase()}. In production, processed via Polar.`
          );
        }
        return;
      }

      setError(data.message || data.error || 'Checkout failed');
    } catch (err: unknown) {
      const e = err as { message?: string };
      setError(e.message || 'Network error');
    } finally {
      setLoading(null);
    }
  };

  const formatPrice = (usd: number, ron: number) => {
    if (usd === 0) return locale === 'ro' ? 'Gratis' : 'Free';
    return locale === 'ro' ? `${ron} RON` : `$${usd}`;
  };

  const formatLimit = (value: number, suffix: string) => {
    if (value === -1) return locale === 'ro' ? 'Nelimitat' : 'Unlimited';
    return `${value} ${suffix}`;
  };

  const plansArray: PlanId[] = ['free', 'pro', 'elite'];

  return (
    <div className="space-y-8">
      {/* Current usage summary */}
      {userUsage && currentPlan === 'free' && (
        <div className="rounded-xl border border-primary/30 bg-primary/5 p-4">
          <p className="mb-3 text-sm font-medium">
            {locale === 'ro' ? 'Utilizare curentă (Plan Free)' : 'Current usage (Free Plan)'}
          </p>
          <div className="grid grid-cols-2 gap-3 text-xs md:grid-cols-4">
            <UsageStat
              label={locale === 'ro' ? 'Trades/lună' : 'Trades/month'}
              used={userUsage.monthly.tradeImport || 0}
              limit={50}
            />
            <UsageStat
              label={locale === 'ro' ? 'Voice/zi' : 'Voice/day'}
              used={userUsage.daily.voiceJournal || 0}
              limit={3}
            />
            <UsageStat
              label={locale === 'ro' ? 'Review AI/lună' : 'AI Reviews/month'}
              used={userUsage.monthly.tradeReview || 0}
              limit={10}
            />
            <UsageStat
              label={locale === 'ro' ? 'Coach/lună' : 'Coach/month'}
              used={userUsage.monthly.coachReport || 0}
              limit={4}
            />
          </div>
        </div>
      )}

      {/* Billing period toggle */}
      <div className="flex justify-center">
        <div className="inline-flex rounded-lg border border-border bg-card p-1">
          <button
            onClick={() => setBillingPeriod('monthly')}
            className={cn(
              'rounded-md px-4 py-1.5 text-sm font-medium transition-colors',
              billingPeriod === 'monthly'
                ? 'bg-primary text-primary-foreground'
                : 'text-muted-foreground hover:text-foreground'
            )}
          >
            {locale === 'ro' ? 'Lunar' : 'Monthly'}
          </button>
          <button
            onClick={() => setBillingPeriod('yearly')}
            className={cn(
              'rounded-md px-4 py-1.5 text-sm font-medium transition-colors',
              billingPeriod === 'yearly'
                ? 'bg-primary text-primary-foreground'
                : 'text-muted-foreground hover:text-foreground'
            )}
          >
            {locale === 'ro' ? 'Anual' : 'Yearly'}
            <span className="ml-2 rounded bg-profit/20 px-1.5 py-0.5 text-[10px] font-bold text-profit">
              -28%
            </span>
          </button>
        </div>
      </div>

      {error && (
        <div className="rounded-lg border border-primary/30 bg-primary/5 p-4 text-sm">
          {error}
        </div>
      )}

      {/* Plan cards */}
      <div className="grid gap-6 md:grid-cols-3">
        {plansArray.map((planId) => {
          const plan = PLANS[planId];
          const price = plan.prices[billingPeriod];
          const isCurrent = currentPlan === planId;
          const isPopular = planId === 'pro';
          const Icon = planId === 'free' ? Zap : planId === 'pro' ? Sparkles : Crown;

          return (
            <div
              key={planId}
              className={cn(
                'relative rounded-2xl border p-6 transition-all',
                isPopular
                  ? 'border-primary bg-primary/5 shadow-lg shadow-primary/20 md:scale-105'
                  : 'border-border bg-card'
              )}
            >
              {isPopular && (
                <div className="absolute -top-3 left-1/2 -translate-x-1/2 rounded-full bg-primary px-3 py-1 text-xs font-bold text-primary-foreground">
                  {locale === 'ro' ? 'Cel mai popular' : 'Most popular'}
                </div>
              )}

              <div className="mb-6 flex items-center gap-2">
                <Icon className={cn('h-5 w-5', isPopular ? 'text-primary' : 'text-muted-foreground')} />
                <h3 className="text-lg font-bold">{plan.name}</h3>
              </div>

              <div className="mb-6">
                <div className="flex items-baseline gap-1">
                  <span className="text-4xl font-bold">{formatPrice(price.usd, price.ron)}</span>
                  {price.usd > 0 && (
                    <span className="text-sm text-muted-foreground">
                      /{billingPeriod === 'monthly' ? (locale === 'ro' ? 'lună' : 'mo') : (locale === 'ro' ? 'an' : 'yr')}
                    </span>
                  )}
                </div>
                {billingPeriod === 'yearly' && price.usd > 0 && (
                  <p className="mt-1 text-xs text-muted-foreground">
                    {locale === 'ro' ? 'echiv.' : 'equiv.'}{' '}
                    {locale === 'ro' ? `${Math.round(price.ron / 12)} RON/lună` : `$${Math.round(price.usd / 12)}/mo`}
                  </p>
                )}
              </div>

              {/* Features */}
              <ul className="mb-6 space-y-2 text-sm">
                <Feature
                  enabled
                  text={formatLimit(plan.limits.maxTradesPerMonth, locale === 'ro' ? 'tranzacții/lună' : 'trades/month')}
                />
                <Feature
                  enabled
                  text={formatLimit(plan.limits.maxVoiceJournalsPerDay, locale === 'ro' ? 'jurnale vocale/zi' : 'voice journals/day')}
                />
                <Feature
                  enabled
                  text={formatLimit(plan.limits.maxTradeReviewsPerMonth, locale === 'ro' ? 'analize AI/lună' : 'AI reviews/month')}
                />
                <Feature
                  enabled
                  text={formatLimit(plan.limits.maxCoachReportsPerMonth, locale === 'ro' ? 'coach AI/lună' : 'AI coaches/month')}
                />
                <Feature
                  enabled
                  text={formatLimit(plan.limits.maxBrokerConnections, locale === 'ro' ? 'conexiuni broker' : 'broker connections')}
                />
                <Feature
                  enabled={plan.limits.fiscalModuleFull}
                  text={locale === 'ro' ? 'Modul fiscal România complet' : 'Full Romania fiscal module'}
                />
                <Feature
                  enabled={plan.limits.semanticSearch}
                  text={locale === 'ro' ? 'Căutare semantică în jurnal' : 'Semantic journal search'}
                />
                <Feature
                  enabled={plan.limits.mt5DesktopBridge}
                  text={locale === 'ro' ? 'MT5 Desktop Bridge' : 'MT5 Desktop Bridge'}
                />
                <Feature
                  enabled={plan.limits.aiMarketScanner}
                  text={locale === 'ro' ? 'AI Market Scanner' : 'AI Market Scanner'}
                />
                <Feature
                  enabled={plan.limits.apiAccess}
                  text={locale === 'ro' ? 'Acces API' : 'API access'}
                />
                <Feature
                  enabled={plan.limits.priorityAI}
                  text={locale === 'ro' ? 'AI prioritar (latență mică)' : 'Priority AI (low latency)'}
                />
              </ul>

              {/* CTA */}
              {isCurrent ? (
                <Button className="w-full" variant="outline" disabled>
                  {locale === 'ro' ? 'Planul tău curent' : 'Your current plan'}
                </Button>
              ) : planId === 'free' ? (
                <Button className="w-full" variant="outline" disabled>
                  {locale === 'ro' ? 'Gratuit pentru totdeauna' : 'Free forever'}
                </Button>
              ) : (
                <Button
                  className="w-full"
                  onClick={() => handleCheckout(planId)}
                  disabled={loading !== null}
                >
                  {loading === planId ? (
                    <Loader2 className="h-4 w-4 animate-spin" />
                  ) : (
                    <>
                      {locale === 'ro' ? 'Upgrade la' : 'Upgrade to'} {plan.name}
                    </>
                  )}
                </Button>
              )}
            </div>
          );
        })}
      </div>

      {/* Trust indicators */}
      <div className="rounded-xl border border-border bg-card p-6 text-center">
        <p className="text-sm text-muted-foreground">
          {locale === 'ro'
            ? '✓ Fără contract · ✓ Anulare oricând · ✓ TVA UE inclus · ✓ Plăți securizate via Polar.sh (Stripe)'
            : '✓ No contract · ✓ Cancel anytime · ✓ EU VAT included · ✓ Secure payments via Polar.sh (Stripe)'}
        </p>
      </div>
    </div>
  );
}

function Feature({ enabled, text }: { enabled: boolean; text: string }) {
  return (
    <li className="flex items-start gap-2">
      {enabled ? (
        <Check className="mt-0.5 h-4 w-4 shrink-0 text-profit" />
      ) : (
        <X className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground/50" />
      )}
      <span className={cn(!enabled && 'text-muted-foreground/50 line-through')}>{text}</span>
    </li>
  );
}

function UsageStat({ label, used, limit }: { label: string; used: number; limit: number }) {
  const pct = Math.min(100, (used / limit) * 100);
  const color = pct >= 90 ? 'bg-loss' : pct >= 70 ? 'bg-orange-500' : 'bg-primary';

  return (
    <div>
      <div className="mb-1 flex items-center justify-between">
        <span className="text-muted-foreground">{label}</span>
        <span className="font-mono font-semibold">
          {used}/{limit}
        </span>
      </div>
      <div className="h-1.5 overflow-hidden rounded-full bg-muted">
        <div className={cn('h-full transition-all', color)} style={{ width: `${pct}%` }} />
      </div>
    </div>
  );
}
