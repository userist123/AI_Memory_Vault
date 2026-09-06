'use client';

import { useEffect, useState } from 'react';
import { useLocale } from 'next-intl';
import { Link } from '@/lib/i18n/routing';
import { cn } from '@/lib/utils';
import { Sparkles } from 'lucide-react';

interface UsageData {
  plan: 'free' | 'pro' | 'elite';
  limits: {
    maxTradesPerMonth: number;
    maxVoiceJournalsPerDay: number;
    maxTradeReviewsPerMonth: number;
    maxCoachReportsPerMonth: number;
  };
  usage: {
    daily: Record<string, number>;
    monthly: Record<string, number>;
  };
}

export function UsageIndicator() {
  const locale = useLocale() as 'ro' | 'en';
  const [data, setData] = useState<UsageData | null>(null);

  useEffect(() => {
    const load = async () => {
      try {
        const res = await fetch('/api/billing/usage');
        if (res.ok) {
          const d = await res.json();
          setData(d);
        }
      } catch {}
    };
    load();
    // Refresh every 30 seconds
    const interval = setInterval(load, 30000);
    return () => clearInterval(interval);
  }, []);

  if (!data || data.plan !== 'free') {
    // Show nothing for Pro/Elite (they're unlimited)
    return null;
  }

  const items = [
    {
      label: locale === 'ro' ? 'Trades' : 'Trades',
      used: data.usage.monthly.tradeImport || 0,
      limit: data.limits.maxTradesPerMonth,
    },
    {
      label: locale === 'ro' ? 'Voice' : 'Voice',
      used: data.usage.daily.voiceJournal || 0,
      limit: data.limits.maxVoiceJournalsPerDay,
    },
    {
      label: 'AI Review',
      used: data.usage.monthly.tradeReview || 0,
      limit: data.limits.maxTradeReviewsPerMonth,
    },
    {
      label: 'Coach',
      used: data.usage.monthly.coachReport || 0,
      limit: data.limits.maxCoachReportsPerMonth,
    },
  ];

  return (
    <div className="border-t border-border/40 p-4">
      <div className="mb-2 flex items-center justify-between">
        <p className="text-[10px] font-semibold uppercase text-muted-foreground">
          {locale === 'ro' ? 'Utilizare' : 'Usage'}
        </p>
        <Link
          href="/pricing"
          className="flex items-center gap-1 text-[10px] font-semibold text-primary hover:underline"
        >
          <Sparkles className="h-2.5 w-2.5" />
          {locale === 'ro' ? 'Upgrade' : 'Upgrade'}
        </Link>
      </div>
      <div className="space-y-2">
        {items.map((item) => (
          <UsageBar key={item.label} {...item} />
        ))}
      </div>
    </div>
  );
}

function UsageBar({ label, used, limit }: { label: string; used: number; limit: number }) {
  const pct = Math.min(100, (used / limit) * 100);
  const color = pct >= 90 ? 'bg-loss' : pct >= 70 ? 'bg-orange-500' : 'bg-primary';

  return (
    <div>
      <div className="mb-1 flex items-center justify-between text-[10px]">
        <span className="text-muted-foreground">{label}</span>
        <span className="font-mono font-semibold">
          {used}/{limit}
        </span>
      </div>
      <div className="h-1 overflow-hidden rounded-full bg-muted">
        <div className={cn('h-full transition-all', color)} style={{ width: `${pct}%` }} />
      </div>
    </div>
  );
}
