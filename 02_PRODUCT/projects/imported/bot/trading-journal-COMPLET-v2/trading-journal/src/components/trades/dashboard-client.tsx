'use client';

import { useEffect, useState } from 'react';
import { useLocale, useTranslations } from 'next-intl';
import { formatCurrency, formatPercent } from '@/lib/utils';
import { Link } from '@/lib/i18n/routing';
import { Button } from '@/components/ui/button';
import type { Trade } from '@/types/trade';
import {
  Activity,
  Target,
  Zap,
  Shield,
  ArrowUpRight,
  ArrowDownRight,
  Upload,
  Mic,
  Loader2,
} from 'lucide-react';

interface DashboardStats {
  totalTrades: number;
  closedTrades: number;
  openTrades: number;
  totalPnL: number;
  winRate: number;
  avgWin: number;
  avgLoss: number;
  profitFactor: number;
}

export function DashboardClient() {
  const locale = useLocale() as 'ro' | 'en';
  const t = useTranslations('dashboard');

  const [stats, setStats] = useState<DashboardStats | null>(null);
  const [recentTrades, setRecentTrades] = useState<Trade[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const load = async () => {
      try {
        const res = await fetch('/api/trades/list?stats=true&limit=5');
        if (res.ok) {
          const data = await res.json();
          setStats(data.stats);
          setRecentTrades(data.trades || []);
        }
      } catch (err) {
        console.error('[Dashboard] Load error:', err);
      } finally {
        setLoading(false);
      }
    };

    load();
  }, []);

  if (loading) {
    return (
      <div className="flex h-64 items-center justify-center">
        <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  const hasData = stats && stats.totalTrades > 0;
  const localeString = locale === 'ro' ? 'ro-RO' : 'en-US';

  const statCards = hasData
    ? [
        {
          label: t('stats.totalPnL'),
          value: formatCurrency(stats.totalPnL, 'USD', localeString),
          valueClass: stats.totalPnL >= 0 ? 'text-profit' : 'text-loss',
          icon: Activity,
          trend: stats.totalPnL >= 0 ? 'up' : 'down',
        },
        {
          label: t('stats.winRate'),
          value: formatPercent(stats.winRate, localeString),
          icon: Target,
          trend: stats.winRate >= 50 ? 'up' : 'down',
        },
        {
          label: t('stats.profitFactor'),
          value: isFinite(stats.profitFactor) ? stats.profitFactor.toFixed(2) : '∞',
          icon: Zap,
          trend: stats.profitFactor >= 1 ? 'up' : 'down',
        },
        {
          label: t('stats.totalTrades'),
          value: stats.totalTrades.toString(),
          icon: Shield,
          trend: 'up' as const,
        },
      ]
    : [];

  return (
    <div>
      <div className="mb-8">
        <h1 className="text-3xl font-bold tracking-tight">{t('title')}</h1>
        <p className="text-sm text-muted-foreground">
          {t('lastUpdate', {
            time: new Date().toLocaleTimeString(localeString),
          })}
        </p>
      </div>

      {!hasData ? (
        <div className="rounded-xl border border-dashed border-border bg-card p-12 text-center">
          <div className="mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-full bg-primary/10">
            <Upload className="h-8 w-8 text-primary" />
          </div>
          <h2 className="text-xl font-semibold">
            {locale === 'ro' ? 'Începe prin a importa tranzacții' : 'Start by importing trades'}
          </h2>
          <p className="mx-auto mt-2 max-w-md text-sm text-muted-foreground">
            {locale === 'ro'
              ? 'Importă din MT5, Binance, Trading 212, XTB sau orice CSV. Detectăm automat formatul.'
              : 'Import from MT5, Binance, Trading 212, XTB or any CSV. We auto-detect the format.'}
          </p>
          <div className="mt-6 flex justify-center gap-3">
            <Link href="/trades">
              <Button size="lg" className="gap-2">
                <Upload className="h-4 w-4" />
                {locale === 'ro' ? 'Importă tranzacții' : 'Import trades'}
              </Button>
            </Link>
            <Link href="/journal">
              <Button size="lg" variant="outline" className="gap-2">
                <Mic className="h-4 w-4" />
                {locale === 'ro' ? 'Jurnal vocal' : 'Voice journal'}
              </Button>
            </Link>
          </div>
        </div>
      ) : (
        <>
          {/* Stats grid */}
          <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
            {statCards.map((card) => (
              <div
                key={card.label}
                className="rounded-xl border border-border bg-card p-6"
              >
                <div className="flex items-center justify-between">
                  <p className="text-sm font-medium text-muted-foreground">
                    {card.label}
                  </p>
                  <card.icon className="h-4 w-4 text-muted-foreground" />
                </div>
                <div className="mt-3 flex items-baseline gap-2">
                  <p className={`text-2xl font-bold ${card.valueClass || ''}`}>
                    {card.value}
                  </p>
                  {card.trend === 'up' ? (
                    <ArrowUpRight className="h-4 w-4 text-profit" />
                  ) : (
                    <ArrowDownRight className="h-4 w-4 text-loss" />
                  )}
                </div>
              </div>
            ))}
          </div>

          {/* Recent trades */}
          <div className="mt-8 rounded-xl border border-border bg-card p-6">
            <div className="mb-4 flex items-center justify-between">
              <h3 className="font-semibold">{t('recentTrades')}</h3>
              <Link href="/trades">
                <Button variant="ghost" size="sm">
                  {locale === 'ro' ? 'Vezi toate' : 'View all'}
                </Button>
              </Link>
            </div>

            <div className="space-y-2">
              {recentTrades.map((trade, i) => (
                <div
                  key={trade._id || i}
                  className="flex items-center justify-between rounded-lg border border-border/40 p-3 text-sm"
                >
                  <div className="flex items-center gap-3">
                    <span
                      className={`inline-flex h-8 w-8 items-center justify-center rounded ${
                        trade.direction === 'long'
                          ? 'bg-profit/10 text-profit'
                          : 'bg-loss/10 text-loss'
                      }`}
                    >
                      {trade.direction === 'long' ? (
                        <ArrowUpRight className="h-4 w-4" />
                      ) : (
                        <ArrowDownRight className="h-4 w-4" />
                      )}
                    </span>
                    <div>
                      <p className="font-mono font-semibold">{trade.symbol}</p>
                      <p className="text-xs text-muted-foreground">
                        {trade.broker} • {trade.assetClass}
                      </p>
                    </div>
                  </div>
                  <div className="text-right">
                    {trade.pnl !== null && trade.pnl !== undefined ? (
                      <p
                        className={`font-mono font-semibold ${
                          trade.pnl >= 0 ? 'text-profit' : 'text-loss'
                        }`}
                      >
                        {trade.pnl >= 0 ? '+' : ''}
                        {trade.pnl.toFixed(2)} {trade.currency}
                      </p>
                    ) : (
                      <p className="text-xs text-muted-foreground">
                        {locale === 'ro' ? 'Deschisă' : 'Open'}
                      </p>
                    )}
                  </div>
                </div>
              ))}
            </div>
          </div>
        </>
      )}
    </div>
  );
}
