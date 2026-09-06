'use client';

import { useEffect, useState } from 'react';
import { useLocale } from 'next-intl';
import type { CoachReport } from '@/types/ai-review';
import { Button } from '@/components/ui/button';
import { formatCurrency, formatPercent } from '@/lib/utils';
import {
  Brain,
  Loader2,
  TrendingUp,
  TrendingDown,
  Minus,
  CheckCircle2,
  AlertTriangle,
  Target,
  Sparkles,
  Calendar,
} from 'lucide-react';
import { cn } from '@/lib/utils';

export function WeeklyCoach() {
  const locale = useLocale() as 'ro' | 'en';
  const [report, setReport] = useState<CoachReport | null>(null);
  const [loading, setLoading] = useState(true);
  const [generating, setGenerating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [periodDays, setPeriodDays] = useState<number>(7);

  useEffect(() => {
    const loadLatest = async () => {
      try {
        const res = await fetch('/api/ai/coach');
        if (res.ok) {
          const data = await res.json();
          setReport(data.report);
        }
      } finally {
        setLoading(false);
      }
    };
    loadLatest();
  }, []);

  const generate = async () => {
    setGenerating(true);
    setError(null);
    try {
      const res = await fetch('/api/ai/coach', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ language: locale, periodDays }),
      });
      const data = await res.json();

      if (data.error === 'no_trades') {
        setError(data.message);
        return;
      }

      if (!res.ok) {
        throw new Error(data.error || 'Coach report failed');
      }

      setReport(data.report);
    } catch (err: unknown) {
      const e = err as { message?: string };
      setError(e.message || 'Unknown error');
    } finally {
      setGenerating(false);
    }
  };

  const t = {
    title: locale === 'ro' ? 'Coach Săptămânal AI' : 'Weekly AI Coach',
    subtitle:
      locale === 'ro'
        ? 'Analiză completă + plan de acțiune. Rulează săptămânal sau la cerere.'
        : 'Complete analysis + action plan. Run weekly or on demand.',
    generate:
      locale === 'ro' ? 'Generează raport nou' : 'Generate new report',
    generating:
      locale === 'ro' ? 'Se analizează...' : 'Analyzing...',
    noReport:
      locale === 'ro'
        ? 'Nu ai încă un raport. Apasă butonul pentru a genera primul tău raport AI.'
        : 'No report yet. Click the button to generate your first AI report.',
    days: locale === 'ro' ? 'zile' : 'days',
  };

  if (loading) {
    return (
      <div className="flex h-48 items-center justify-center rounded-xl border border-border bg-card">
        <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Header with generate button */}
      <div className="rounded-xl border border-primary/30 bg-gradient-to-br from-primary/10 via-primary/5 to-transparent p-6">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
          <div className="flex items-center gap-3">
            <div className="flex h-12 w-12 items-center justify-center rounded-lg bg-primary/20 text-primary">
              <Brain className="h-6 w-6" />
            </div>
            <div>
              <h2 className="text-lg font-bold">{t.title}</h2>
              <p className="text-sm text-muted-foreground">{t.subtitle}</p>
            </div>
          </div>

          <div className="flex flex-wrap gap-2">
            <select
              value={periodDays}
              onChange={(e) => setPeriodDays(Number(e.target.value))}
              className="rounded-md border border-input bg-background px-3 py-2 text-sm"
              disabled={generating}
            >
              <option value={1}>1 {t.days}</option>
              <option value={7}>7 {t.days}</option>
              <option value={14}>14 {t.days}</option>
              <option value={30}>30 {t.days}</option>
              <option value={90}>90 {t.days}</option>
            </select>
            <Button onClick={generate} disabled={generating} className="gap-2">
              {generating ? (
                <Loader2 className="h-4 w-4 animate-spin" />
              ) : (
                <Sparkles className="h-4 w-4" />
              )}
              {generating ? t.generating : t.generate}
            </Button>
          </div>
        </div>
      </div>

      {error && (
        <div className="rounded-lg border border-orange-500/30 bg-orange-500/10 p-4 text-sm">
          {error}
        </div>
      )}

      {!report && !error && (
        <div className="rounded-xl border border-dashed border-border bg-card p-12 text-center">
          <Brain className="mx-auto h-10 w-10 text-muted-foreground/50" />
          <p className="mt-3 text-sm text-muted-foreground">{t.noReport}</p>
        </div>
      )}

      {report && <CoachReportDisplay report={report} />}
    </div>
  );
}

function CoachReportDisplay({ report }: { report: CoachReport }) {
  const locale = useLocale() as 'ro' | 'en';
  const localeStr = locale === 'ro' ? 'ro-RO' : 'en-US';

  const gradeColors: Record<string, string> = {
    A: 'bg-profit text-white',
    B: 'bg-primary text-primary-foreground',
    C: 'bg-yellow-500 text-white',
    D: 'bg-orange-500 text-white',
    F: 'bg-loss text-white',
  };

  const momentumIcon = {
    improving: <TrendingUp className="h-5 w-5 text-profit" />,
    stable: <Minus className="h-5 w-5 text-muted-foreground" />,
    declining: <TrendingDown className="h-5 w-5 text-loss" />,
  };

  const momentumLabel = {
    improving: { ro: 'În îmbunătățire', en: 'Improving' },
    stable: { ro: 'Stabil', en: 'Stable' },
    declining: { ro: 'În declin', en: 'Declining' },
  };

  const priorityColors = {
    critical: 'border-loss/50 bg-loss/10 text-loss',
    high: 'border-orange-500/50 bg-orange-500/10 text-orange-500',
    medium: 'border-primary/50 bg-primary/10 text-primary',
    low: 'border-muted bg-muted/50 text-muted-foreground',
  };

  const priorityLabel: Record<string, { ro: string; en: string }> = {
    critical: { ro: 'Critic', en: 'Critical' },
    high: { ro: 'Important', en: 'High' },
    medium: { ro: 'Mediu', en: 'Medium' },
    low: { ro: 'Scăzut', en: 'Low' },
  };

  return (
    <div className="space-y-6">
      {/* Headline card */}
      <div className="rounded-xl border border-border bg-card p-6">
        <div className="flex items-start gap-4">
          <div
            className={cn(
              'flex h-20 w-20 shrink-0 items-center justify-center rounded-xl text-4xl font-bold',
              gradeColors[report.grade]
            )}
          >
            {report.grade}
          </div>
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2 text-xs text-muted-foreground">
              <Calendar className="h-3 w-3" />
              <span>
                {new Date(report.periodStart).toLocaleDateString(localeStr)} —{' '}
                {new Date(report.periodEnd).toLocaleDateString(localeStr)}
              </span>
              <span>•</span>
              <span className="flex items-center gap-1">
                {momentumIcon[report.momentum]}
                {momentumLabel[report.momentum][locale]}
              </span>
            </div>
            <h3 className="mt-2 text-xl font-bold leading-tight">
              {report.headline}
            </h3>
            <p className="mt-2 text-sm text-muted-foreground leading-relaxed">
              {report.summary}
            </p>
          </div>
        </div>
      </div>

      {/* Stats grid */}
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <Stat
          label={locale === 'ro' ? 'P/P total' : 'Total P&L'}
          value={formatCurrency(report.stats.totalPnL, 'USD', localeStr)}
          valueClass={report.stats.totalPnL >= 0 ? 'text-profit' : 'text-loss'}
        />
        <Stat
          label={locale === 'ro' ? 'Rată succes' : 'Win rate'}
          value={formatPercent(report.stats.winRate, localeStr)}
        />
        <Stat
          label={locale === 'ro' ? 'Factor profit' : 'Profit factor'}
          value={report.stats.profitFactor > 99 ? '∞' : report.stats.profitFactor.toFixed(2)}
        />
        <Stat
          label={locale === 'ro' ? 'Tranzacții' : 'Trades'}
          value={report.stats.totalTrades.toString()}
        />
      </div>

      {/* Strengths & Weaknesses */}
      <div className="grid gap-4 md:grid-cols-2">
        {report.strengths.length > 0 && (
          <div className="rounded-xl border border-profit/30 bg-profit/5 p-4">
            <h3 className="mb-3 flex items-center gap-2 font-semibold">
              <CheckCircle2 className="h-4 w-4 text-profit" />
              {locale === 'ro' ? 'Puncte tari' : 'Strengths'}
            </h3>
            <ul className="space-y-1.5 text-sm">
              {report.strengths.map((s, i) => (
                <li key={i} className="flex gap-2">
                  <span className="text-profit">✓</span>
                  <span>{s}</span>
                </li>
              ))}
            </ul>
          </div>
        )}

        {report.weaknesses.length > 0 && (
          <div className="rounded-xl border border-orange-500/30 bg-orange-500/5 p-4">
            <h3 className="mb-3 flex items-center gap-2 font-semibold">
              <AlertTriangle className="h-4 w-4 text-orange-500" />
              {locale === 'ro' ? 'Puncte slabe' : 'Weaknesses'}
            </h3>
            <ul className="space-y-1.5 text-sm">
              {report.weaknesses.map((w, i) => (
                <li key={i} className="flex gap-2">
                  <span className="text-orange-500">!</span>
                  <span>{w}</span>
                </li>
              ))}
            </ul>
          </div>
        )}
      </div>

      {/* Patterns */}
      {report.patterns.length > 0 && (
        <div className="rounded-xl border border-border bg-card p-4">
          <h3 className="mb-3 font-semibold">
            {locale === 'ro' ? 'Pattern-uri comportamentale' : 'Behavioral patterns'}
          </h3>
          <div className="space-y-3">
            {report.patterns.map((p, i) => (
              <div
                key={i}
                className="rounded-lg border border-border/40 bg-muted/20 p-3"
              >
                <div className="flex items-start justify-between gap-2">
                  <p className="font-medium text-sm">{p.description}</p>
                  <div className="flex gap-0.5 shrink-0">
                    {Array.from({ length: 5 }).map((_, j) => (
                      <div
                        key={j}
                        className={cn(
                          'h-1.5 w-1.5 rounded-full',
                          j < p.severity
                            ? p.severity >= 4
                              ? 'bg-loss'
                              : p.severity >= 3
                                ? 'bg-orange-500'
                                : 'bg-primary'
                            : 'bg-muted'
                        )}
                      />
                    ))}
                  </div>
                </div>
                <p className="mt-1 text-xs text-muted-foreground">{p.evidence}</p>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Action Plan */}
      {report.actionPlan.length > 0 && (
        <div className="rounded-xl border border-primary/30 bg-primary/5 p-4">
          <h3 className="mb-3 flex items-center gap-2 font-semibold text-primary">
            <Target className="h-4 w-4" />
            {locale === 'ro' ? 'Plan de acțiune' : 'Action plan'}
          </h3>
          <div className="space-y-3">
            {report.actionPlan.map((a, i) => (
              <div key={i} className="flex gap-3 rounded-lg bg-background p-3">
                <span
                  className={cn(
                    'shrink-0 rounded-md border px-2 py-0.5 text-xs font-semibold uppercase',
                    priorityColors[a.priority]
                  )}
                >
                  {priorityLabel[a.priority][locale]}
                </span>
                <div className="flex-1 min-w-0">
                  <p className="font-medium text-sm">{a.action}</p>
                  <p className="mt-1 text-xs text-muted-foreground">{a.rationale}</p>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Footer */}
      <div className="flex items-center justify-between text-xs text-muted-foreground">
        <span>
          {locale === 'ro' ? 'Generat cu' : 'Generated with'} {report.provider} ({report.model})
        </span>
        <span>
          {new Date(report.createdAt).toLocaleString(localeStr)}
        </span>
      </div>
    </div>
  );
}

function Stat({
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
      <p className={cn('mt-1 text-xl font-bold', valueClass)}>{value}</p>
    </div>
  );
}
