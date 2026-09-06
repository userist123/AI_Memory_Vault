'use client';

import { useEffect, useState } from 'react';
import { useLocale } from 'next-intl';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import {
  Loader2,
  Zap,
  TrendingUp,
  TrendingDown,
  CheckCircle2,
  XCircle,
  AlertTriangle,
  RefreshCw,
} from 'lucide-react';
import type { StoredAlert } from '@/lib/db/alerts';

export function SignalsView() {
  const locale = useLocale() as 'ro' | 'en';
  const [alerts, setAlerts] = useState<StoredAlert[]>([]);
  const [loading, setLoading] = useState(true);
  const [scanning, setScanning] = useState(false);
  const [filter, setFilter] = useState<'pending' | 'executed' | 'skipped' | 'all'>('pending');
  const [error, setError] = useState<string | null>(null);

  const load = async () => {
    try {
      const url = filter === 'all' ? '/api/signals/list' : `/api/signals/list?status=${filter}`;
      const res = await fetch(url);
      if (res.ok) {
        const data = await res.json();
        setAlerts(data.alerts || []);
      }
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    load();
  }, [filter]);

  const scan = async () => {
    setScanning(true);
    setError(null);
    try {
      const res = await fetch('/api/signals/scan', { method: 'POST' });
      const data = await res.json();
      if (!res.ok) {
        setError(data.message || data.error);
        return;
      }
      await load();
    } catch (err: unknown) {
      const e = err as { message?: string };
      setError(e.message || 'Scan failed');
    } finally {
      setScanning(false);
    }
  };

  const skipAlert = async (signalId: string) => {
    await fetch(`/api/signals/execute?signalId=${signalId}&reason=manually_skipped`, {
      method: 'DELETE',
    });
    await load();
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex gap-2">
          {(['pending', 'executed', 'skipped', 'all'] as const).map((f) => (
            <button
              key={f}
              onClick={() => setFilter(f)}
              className={cn(
                'rounded-full border px-3 py-1.5 text-xs font-medium transition-colors',
                filter === f
                  ? 'border-primary bg-primary text-primary-foreground'
                  : 'border-border bg-background hover:border-primary/50'
              )}
            >
              {locale === 'ro'
                ? { pending: 'Active', executed: 'Executate', skipped: 'Skippate', all: 'Toate' }[f]
                : { pending: 'Pending', executed: 'Executed', skipped: 'Skipped', all: 'All' }[f]}
            </button>
          ))}
        </div>

        <Button onClick={scan} disabled={scanning} className="gap-2">
          {scanning ? <Loader2 className="h-4 w-4 animate-spin" /> : <RefreshCw className="h-4 w-4" />}
          {locale === 'ro' ? 'Scanează acum' : 'Scan now'}
        </Button>
      </div>

      {error && (
        <div className="flex items-start gap-2 rounded-lg border border-destructive/50 bg-destructive/10 p-4 text-sm">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-destructive" />
          <p className="text-destructive">{error}</p>
        </div>
      )}

      {loading ? (
        <div className="flex h-48 items-center justify-center rounded-xl border border-border bg-card">
          <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
        </div>
      ) : alerts.length === 0 ? (
        <div className="flex h-64 flex-col items-center justify-center gap-3 rounded-xl border border-dashed border-border bg-card p-6 text-center">
          <Zap className="h-10 w-10 text-muted-foreground/50" />
          <p className="text-sm text-muted-foreground">
            {filter === 'pending'
              ? locale === 'ro'
                ? 'Niciun semnal activ. Apasă "Scanează acum" pentru a căuta setup-uri.'
                : 'No active signals. Click "Scan now" to find setups.'
              : locale === 'ro'
                ? 'Niciun semnal în această categorie.'
                : 'No signals in this category.'}
          </p>
        </div>
      ) : (
        <div className="space-y-3">
          {alerts.map((alert) => (
            <SignalCard
              key={alert.signalId}
              alert={alert}
              onSkip={() => skipAlert(alert.signalId)}
              onExecuted={load}
            />
          ))}
        </div>
      )}
    </div>
  );
}

function SignalCard({
  alert,
  onSkip,
  onExecuted,
}: {
  alert: StoredAlert;
  onSkip: () => void;
  onExecuted: () => void;
}) {
  const locale = useLocale() as 'ro' | 'en';
  const [executing, setExecuting] = useState(false);
  const [reason, setReason] = useState('');
  const [risk, setRisk] = useState(1);
  const [testnet, setTestnet] = useState(true);
  const [showExecForm, setShowExecForm] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const s = alert.signal;
  const isLong = s.direction === 'long';
  const isPending = alert.status === 'pending';

  const execute = async () => {
    if (reason.trim().length < 3) {
      setError(locale === 'ro' ? 'Motivul e obligatoriu (min 3 caractere)' : 'Reason required (min 3 chars)');
      return;
    }

    setExecuting(true);
    setError(null);
    try {
      const res = await fetch('/api/signals/execute', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          signalId: alert.signalId,
          brokerId: 'binance',
          testnet,
          reason,
          riskPercentOverride: risk,
        }),
      });
      const data = await res.json();
      if (!res.ok) {
        setError(data.message || data.details || data.error);
        return;
      }
      onExecuted();
    } catch (err: unknown) {
      const e = err as { message?: string };
      setError(e.message || 'Execute failed');
    } finally {
      setExecuting(false);
    }
  };

  return (
    <div className="rounded-xl border border-border bg-card p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex items-start gap-3">
          <div
            className={cn(
              'flex h-10 w-10 shrink-0 items-center justify-center rounded-lg',
              isLong ? 'bg-profit/10 text-profit' : 'bg-loss/10 text-loss'
            )}
          >
            {isLong ? <TrendingUp className="h-5 w-5" /> : <TrendingDown className="h-5 w-5" />}
          </div>
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-2">
              <span className="font-mono font-bold">{s.symbol}</span>
              <span className={cn('rounded px-2 py-0.5 text-xs font-semibold uppercase',
                isLong ? 'bg-profit/10 text-profit' : 'bg-loss/10 text-loss'
              )}>
                {isLong ? 'Long' : 'Short'}
              </span>
              <span className="text-xs text-muted-foreground">{s.timeframe}</span>
              <span className="text-xs font-semibold text-primary">
                {s.strength}/100
              </span>
            </div>
            <p className="mt-1 text-sm text-muted-foreground">{s.reason}</p>
          </div>
        </div>

        {alert.status !== 'pending' && (
          <div className="flex items-center gap-1 text-xs">
            {alert.status === 'executed' ? (
              <><CheckCircle2 className="h-4 w-4 text-profit" /><span className="text-profit">
                {locale === 'ro' ? 'Executat' : 'Executed'}
              </span></>
            ) : alert.status === 'skipped' ? (
              <><XCircle className="h-4 w-4 text-muted-foreground" /><span className="text-muted-foreground">
                {locale === 'ro' ? 'Skipped' : 'Skipped'}
              </span></>
            ) : (
              <span className="text-muted-foreground">{alert.status}</span>
            )}
          </div>
        )}
      </div>

      {/* Levels */}
      <div className="mt-3 grid grid-cols-2 gap-2 md:grid-cols-4">
        <Level label="Entry" value={s.entry.toFixed(4)} />
        <Level label={locale === 'ro' ? 'Stop' : 'Stop'} value={s.stopLoss.toFixed(4)} color="text-loss" />
        <Level label={locale === 'ro' ? 'Target' : 'Target'} value={s.takeProfit.toFixed(4)} color="text-profit" />
        <Level label="R/R" value={`1:${s.riskRewardRatio.toFixed(2)}`} />
      </div>

      {/* Actions */}
      {isPending && !showExecForm && (
        <div className="mt-4 flex gap-2">
          <Button size="sm" onClick={() => setShowExecForm(true)} className="gap-2">
            <CheckCircle2 className="h-4 w-4" />
            {locale === 'ro' ? 'Execută' : 'Execute'}
          </Button>
          <Button size="sm" variant="outline" onClick={onSkip}>
            {locale === 'ro' ? 'Skip' : 'Skip'}
          </Button>
        </div>
      )}

      {isPending && showExecForm && (
        <div className="mt-4 space-y-3 rounded-lg border border-primary/30 bg-primary/5 p-4">
          <div>
            <label className="text-xs font-medium text-muted-foreground">
              {locale === 'ro' ? 'Motivul trade-ului (obligatoriu)' : 'Reason (required)'}
            </label>
            <input
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              placeholder={locale === 'ro' ? 'ex: setup bun, volumul confirmă' : 'e.g. good setup, volume confirms'}
              className="mt-1 w-full rounded border border-input bg-background px-3 py-2 text-sm"
            />
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="text-xs font-medium text-muted-foreground">
                Risk %
              </label>
              <input
                type="number"
                step="0.1"
                min="0.1"
                max="5"
                value={risk}
                onChange={(e) => setRisk(parseFloat(e.target.value))}
                className="mt-1 w-full rounded border border-input bg-background px-3 py-2 text-sm"
              />
            </div>
            <div className="flex items-end">
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={testnet}
                  onChange={(e) => setTestnet(e.target.checked)}
                  className="rounded border-input"
                />
                Testnet
              </label>
            </div>
          </div>

          {error && (
            <div className="rounded border border-destructive/50 bg-destructive/10 p-2 text-xs text-destructive">
              {error}
            </div>
          )}

          <div className="flex gap-2">
            <Button size="sm" onClick={execute} disabled={executing}>
              {executing ? <Loader2 className="h-4 w-4 animate-spin" /> : null}
              {locale === 'ro' ? 'Confirm execuție' : 'Confirm'}
            </Button>
            <Button size="sm" variant="outline" onClick={() => setShowExecForm(false)}>
              {locale === 'ro' ? 'Anulează' : 'Cancel'}
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}

function Level({ label, value, color }: { label: string; value: string; color?: string }) {
  return (
    <div className="rounded border border-border/40 bg-muted/20 p-2">
      <p className="text-[10px] uppercase text-muted-foreground">{label}</p>
      <p className={cn('mt-0.5 font-mono text-sm font-semibold', color)}>{value}</p>
    </div>
  );
}
