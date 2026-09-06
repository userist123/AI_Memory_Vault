'use client';

import { useEffect, useState } from 'react';
import { useLocale, useTranslations } from 'next-intl';
import { formatCurrency, formatDate } from '@/lib/utils';
import type { Trade } from '@/types/trade';
import { Loader2, ArrowUpRight, ArrowDownRight, Inbox } from 'lucide-react';
import { TradeReviewButton } from '@/components/ai/trade-review-button';

interface TradesTableProps {
  refreshKey?: number;
}

export function TradesTable({ refreshKey = 0 }: TradesTableProps) {
  const locale = useLocale() as 'ro' | 'en';
  const t = useTranslations('trades');
  const [trades, setTrades] = useState<Trade[]>([]);
  const [stats, setStats] = useState<{
    totalTrades: number;
    totalPnL: number;
    winRate: number;
    profitFactor: number;
  } | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let mounted = true;
    setLoading(true);

    const load = async () => {
      try {
        const res = await fetch('/api/trades/list?stats=true&limit=100');
        if (!res.ok) return;
        const data = await res.json();
        if (!mounted) return;
        setTrades(data.trades || []);
        setStats(data.stats || null);
      } catch (err) {
        console.error('[Trades] Load error:', err);
      } finally {
        if (mounted) setLoading(false);
      }
    };

    load();
    return () => {
      mounted = false;
    };
  }, [refreshKey]);

  if (loading) {
    return (
      <div className="flex h-64 items-center justify-center rounded-xl border border-border bg-card">
        <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (trades.length === 0) {
    return (
      <div className="flex h-64 flex-col items-center justify-center gap-3 rounded-xl border border-dashed border-border bg-card">
        <Inbox className="h-10 w-10 text-muted-foreground/50" />
        <p className="text-sm text-muted-foreground">
          {locale === 'ro'
            ? 'Nicio tranzacție încă. Importă fișierul de la broker mai sus.'
            : 'No trades yet. Import your broker file above.'}
        </p>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {stats && (
        <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
          <StatCard
            label={locale === 'ro' ? 'Total tranzacții' : 'Total trades'}
            value={stats.totalTrades.toString()}
          />
          <StatCard
            label={locale === 'ro' ? 'P/P total' : 'Total P&L'}
            value={formatCurrency(stats.totalPnL, 'USD', locale === 'ro' ? 'ro-RO' : 'en-US')}
            valueClass={stats.totalPnL >= 0 ? 'text-profit' : 'text-loss'}
          />
          <StatCard
            label={locale === 'ro' ? 'Rată succes' : 'Win rate'}
            value={`${stats.winRate.toFixed(1)}%`}
          />
          <StatCard
            label={locale === 'ro' ? 'Factor profit' : 'Profit factor'}
            value={isFinite(stats.profitFactor) ? stats.profitFactor.toFixed(2) : '∞'}
          />
        </div>
      )}

      <div className="overflow-hidden rounded-xl border border-border bg-card">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="border-b border-border bg-muted/30">
              <tr>
                <th className="px-4 py-3 text-left font-semibold">{t('columns.date')}</th>
                <th className="px-4 py-3 text-left font-semibold">{t('columns.symbol')}</th>
                <th className="px-4 py-3 text-left font-semibold">{t('columns.direction')}</th>
                <th className="px-4 py-3 text-right font-semibold">{t('columns.entry')}</th>
                <th className="px-4 py-3 text-right font-semibold">{t('columns.exit')}</th>
                <th className="px-4 py-3 text-right font-semibold">{t('columns.pnl')}</th>
                <th className="px-4 py-3 text-left font-semibold">Broker</th>
                <th className="px-4 py-3 text-right font-semibold">AI</th>
              </tr>
            </thead>
            <tbody>
              {trades.map((trade, i) => {
                const entryDate =
                  trade.entryTime instanceof Date
                    ? trade.entryTime
                    : new Date(trade.entryTime);
                return (
                  <tr
                    key={trade._id || i}
                    className="border-b border-border/40 transition-colors hover:bg-muted/20"
                  >
                    <td className="px-4 py-3 text-muted-foreground">
                      {formatDate(entryDate, locale === 'ro' ? 'ro-RO' : 'en-US')}
                    </td>
                    <td className="px-4 py-3">
                      <span className="font-mono font-semibold">{trade.symbol}</span>
                      <span className="ml-2 text-xs text-muted-foreground">
                        {trade.assetClass}
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      <span
                        className={`inline-flex items-center gap-1 rounded-md px-2 py-0.5 text-xs font-medium ${
                          trade.direction === 'long'
                            ? 'bg-profit/10 text-profit'
                            : 'bg-loss/10 text-loss'
                        }`}
                      >
                        {trade.direction === 'long' ? (
                          <ArrowUpRight className="h-3 w-3" />
                        ) : (
                          <ArrowDownRight className="h-3 w-3" />
                        )}
                        {trade.direction === 'long' ? 'Long' : 'Short'}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-right font-mono">
                      {trade.entryPrice.toFixed(5)}
                    </td>
                    <td className="px-4 py-3 text-right font-mono">
                      {trade.exitPrice !== null && trade.exitPrice !== undefined
                        ? trade.exitPrice.toFixed(5)
                        : '—'}
                    </td>
                    <td className="px-4 py-3 text-right font-mono">
                      {trade.pnl !== null && trade.pnl !== undefined ? (
                        <span className={trade.pnl >= 0 ? 'text-profit' : 'text-loss'}>
                          {trade.pnl >= 0 ? '+' : ''}
                          {trade.pnl.toFixed(2)} {trade.currency}
                        </span>
                      ) : (
                        <span className="text-muted-foreground">—</span>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      <span className="inline-block rounded bg-muted px-2 py-0.5 text-xs uppercase">
                        {trade.broker}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-right">
                      {trade._id && trade.status === 'closed' && (
                        <TradeReviewButton tradeId={trade._id} />
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

function StatCard({
  label,
  value,
  valueClass,
}: {
  label: string;
  value: string;
  valueClass?: string;
}) {
  return (
    <div className="rounded-lg border border-border bg-card p-4">
      <p className="text-xs text-muted-foreground">{label}</p>
      <p className={`mt-1 text-lg font-bold ${valueClass || ''}`}>{value}</p>
    </div>
  );
}
