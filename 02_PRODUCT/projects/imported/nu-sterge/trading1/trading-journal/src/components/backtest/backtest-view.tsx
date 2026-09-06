'use client';

import { useState } from 'react';
import { useLocale } from 'next-intl';
import { Button } from '@/components/ui/button';
import { Loader2, Play, TrendingUp, AlertTriangle } from 'lucide-react';
import { LineChart, Line, XAxis, YAxis, Tooltip, ResponsiveContainer, ReferenceLine } from 'recharts';
import type { BacktestResult } from '@/lib/backtest/engine';

const POPULAR_SYMBOLS = ['BTCUSDT', 'ETHUSDT', 'SOLUSDT', 'BNBUSDT', 'XRPUSDT'];
const TIMEFRAMES = ['15m', '1h', '4h', '1d'] as const;

export function BacktestView() {
  const locale = useLocale() as 'ro' | 'en';
  const [symbol, setSymbol] = useState('BTCUSDT');
  const [timeframe, setTimeframe] = useState<'15m' | '1h' | '4h' | '1d'>('4h');
  const [bars, setBars] = useState(500);
  const [initialCapital, setInitialCapital] = useState(10000);
  const [riskPct, setRiskPct] = useState(1);

  const [loading, setLoading] = useState(false);
  const [result, setResult] = useState<BacktestResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const run = async () => {
    setLoading(true);
    setError(null);
    setResult(null);
    try {
      const res = await fetch('/api/backtest/run', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          symbol,
          timeframe,
          bars,
          initialCapital,
          riskPerTradePct: riskPct,
          commissionPct: 0.001,
          maxOpenPositions: 1,
        }),
      });
      const data = await res.json();
      if (!res.ok) {
        setError(data.message || data.details || data.error);
        return;
      }
      setResult(data.result);
    } catch (err: unknown) {
      const e = err as { message?: string };
      setError(e.message || 'Failed');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="space-y-6">
      {/* Form */}
      <div className="rounded-xl border border-border bg-card p-6">
        <h2 className="mb-4 font-semibold">
          {locale === 'ro' ? 'Configurare backtest' : 'Backtest config'}
        </h2>
        <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-5">
          <div>
            <label className="text-xs font-medium text-muted-foreground">
              {locale === 'ro' ? 'Simbol' : 'Symbol'}
            </label>
            <select
              value={symbol}
              onChange={(e) => setSymbol(e.target.value)}
              className="mt-1 w-full rounded border border-input bg-background px-3 py-2 text-sm"
            >
              {POPULAR_SYMBOLS.map((s) => (
                <option key={s} value={s}>{s}</option>
              ))}
            </select>
          </div>
          <div>
            <label className="text-xs font-medium text-muted-foreground">
              {locale === 'ro' ? 'Timeframe' : 'Timeframe'}
            </label>
            <select
              value={timeframe}
              onChange={(e) => setTimeframe(e.target.value as typeof timeframe)}
              className="mt-1 w-full rounded border border-input bg-background px-3 py-2 text-sm"
            >
              {TIMEFRAMES.map((t) => <option key={t} value={t}>{t}</option>)}
            </select>
          </div>
          <div>
            <label className="text-xs font-medium text-muted-foreground">
              {locale === 'ro' ? 'Nr. bare' : 'Bars'}
            </label>
            <input
              type="number"
              value={bars}
              min={100}
              max={1000}
              onChange={(e) => setBars(parseInt(e.target.value))}
              className="mt-1 w-full rounded border border-input bg-background px-3 py-2 text-sm"
            />
          </div>
          <div>
            <label className="text-xs font-medium text-muted-foreground">
              {locale === 'ro' ? 'Capital inițial' : 'Initial capital'}
            </label>
            <input
              type="number"
              value={initialCapital}
              onChange={(e) => setInitialCapital(parseFloat(e.target.value))}
              className="mt-1 w-full rounded border border-input bg-background px-3 py-2 text-sm"
            />
          </div>
          <div>
            <label className="text-xs font-medium text-muted-foreground">Risk %</label>
            <input
              type="number"
              step="0.1"
              value={riskPct}
              min={0.1}
              max={5}
              onChange={(e) => setRiskPct(parseFloat(e.target.value))}
              className="mt-1 w-full rounded border border-input bg-background px-3 py-2 text-sm"
            />
          </div>
        </div>

        <Button onClick={run} disabled={loading} className="mt-4 gap-2">
          {loading ? <Loader2 className="h-4 w-4 animate-spin" /> : <Play className="h-4 w-4" />}
          {locale === 'ro' ? 'Rulează backtest' : 'Run backtest'}
        </Button>
      </div>

      {error && (
        <div className="flex items-start gap-2 rounded-lg border border-destructive/50 bg-destructive/10 p-4 text-sm">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-destructive" />
          <p className="text-destructive">{error}</p>
        </div>
      )}

      {result && <BacktestResults result={result} locale={locale} />}
    </div>
  );
}

function BacktestResults({ result, locale }: { result: BacktestResult; locale: 'ro' | 'en' }) {
  const profitable = result.totalPnL > 0;

  return (
    <div className="space-y-4">
      {/* Summary stats */}
      <div className="grid gap-3 md:grid-cols-2 lg:grid-cols-4">
        <StatCard
          label={locale === 'ro' ? 'Profit total' : 'Total P&L'}
          value={`${result.totalPnL >= 0 ? '+' : ''}${result.totalPnL.toFixed(2)} (${result.totalPnLPct.toFixed(1)}%)`}
          color={profitable ? 'text-profit' : 'text-loss'}
        />
        <StatCard
          label={locale === 'ro' ? 'Rată succes' : 'Win rate'}
          value={`${result.winRate.toFixed(1)}% (${result.wins}/${result.totalTrades})`}
        />
        <StatCard
          label={locale === 'ro' ? 'Factor profit' : 'Profit factor'}
          value={result.profitFactor > 99 ? '∞' : result.profitFactor.toFixed(2)}
          color={result.profitFactor >= 1.5 ? 'text-profit' : result.profitFactor >= 1 ? '' : 'text-loss'}
        />
        <StatCard
          label="Max DD"
          value={`-${result.maxDrawdownPct.toFixed(1)}%`}
          color={result.maxDrawdownPct < 15 ? 'text-profit' : result.maxDrawdownPct < 30 ? 'text-orange-500' : 'text-loss'}
        />
      </div>

      {/* Equity curve */}
      {result.equityCurve.length > 0 && (
        <div className="rounded-xl border border-border bg-card p-4">
          <h3 className="mb-3 font-semibold">Equity curve</h3>
          <div style={{ width: '100%', height: 280 }}>
            <ResponsiveContainer>
              <LineChart data={result.equityCurve.map((p) => ({
                time: new Date(p.time).toLocaleDateString(),
                equity: p.equity,
              }))}>
                <XAxis dataKey="time" tick={{ fontSize: 10 }} />
                <YAxis tick={{ fontSize: 10 }} domain={['auto', 'auto']} />
                <Tooltip
                  contentStyle={{ background: '#18181b', border: '1px solid #27272a', borderRadius: 8 }}
                  formatter={(v: number) => v.toFixed(2)}
                />
                <ReferenceLine y={result.initialCapital} stroke="#71717a" strokeDasharray="4 4" />
                <Line
                  type="monotone"
                  dataKey="equity"
                  stroke={profitable ? '#22c55e' : '#ef4444'}
                  strokeWidth={2}
                  dot={false}
                />
              </LineChart>
            </ResponsiveContainer>
          </div>
        </div>
      )}

      {/* Metrics table */}
      <div className="rounded-xl border border-border bg-card p-4">
        <h3 className="mb-3 font-semibold">{locale === 'ro' ? 'Metrici detaliate' : 'Detailed metrics'}</h3>
        <div className="grid gap-2 text-sm md:grid-cols-2">
          <Row label="Avg win" value={result.avgWin.toFixed(2)} color="text-profit" />
          <Row label="Avg loss" value={result.avgLoss.toFixed(2)} color="text-loss" />
          <Row label="Best trade" value={result.bestTrade.toFixed(2)} />
          <Row label="Worst trade" value={result.worstTrade.toFixed(2)} />
          <Row label="Avg R-multiple" value={result.avgRMultiple.toFixed(2) + 'R'} />
          <Row label="Sharpe ratio" value={result.sharpeRatio.toFixed(2)} />
          <Row label={locale === 'ro' ? 'Max câștiguri consecutive' : 'Max consecutive wins'} value={result.maxConsecutiveWins.toString()} />
          <Row label={locale === 'ro' ? 'Max pierderi consecutive' : 'Max consecutive losses'} value={result.maxConsecutiveLosses.toString()} />
        </div>
      </div>

      {/* Warnings */}
      {result.warnings.length > 0 && (
        <div className="rounded-xl border border-orange-500/30 bg-orange-500/5 p-4">
          <h3 className="mb-2 flex items-center gap-2 text-sm font-semibold text-orange-500">
            <AlertTriangle className="h-4 w-4" />
            {locale === 'ro' ? 'Avertizări' : 'Warnings'}
          </h3>
          <ul className="space-y-1 text-sm">
            {result.warnings.map((w, i) => (
              <li key={i} className="text-muted-foreground">• {w}</li>
            ))}
          </ul>
        </div>
      )}

      {/* Disclaimer */}
      <div className="rounded-lg border border-border bg-muted/30 p-4 text-xs text-muted-foreground">
        <strong>{locale === 'ro' ? 'Disclaimer important:' : 'Important disclaimer:'}</strong>{' '}
        {locale === 'ro'
          ? 'Performanța trecută NU garantează rezultate viitoare. Backtest-ul folosește ipoteze optimiste (fill perfect, fără slippage). Rezultate live vor fi probabil cu 20-40% mai slabe. Paper trade minim 3 luni înainte de bani reali.'
          : 'Past performance does NOT guarantee future results. Backtest uses optimistic assumptions (perfect fills, no slippage). Live results will likely be 20-40% worse. Paper trade minimum 3 months before real money.'}
      </div>
    </div>
  );
}

function StatCard({ label, value, color }: { label: string; value: string; color?: string }) {
  return (
    <div className="rounded-lg border border-border bg-card p-4">
      <p className="text-xs text-muted-foreground">{label}</p>
      <p className={`mt-1 font-mono text-lg font-bold ${color || ''}`}>{value}</p>
    </div>
  );
}

function Row({ label, value, color }: { label: string; value: string; color?: string }) {
  return (
    <div className="flex items-center justify-between border-b border-border/40 py-1.5 last:border-0">
      <span className="text-muted-foreground">{label}</span>
      <span className={`font-mono font-medium ${color || ''}`}>{value}</span>
    </div>
  );
}
