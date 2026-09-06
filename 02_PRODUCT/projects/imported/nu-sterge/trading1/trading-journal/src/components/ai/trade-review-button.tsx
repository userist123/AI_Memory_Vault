'use client';

import { useState } from 'react';
import { useLocale, useTranslations } from 'next-intl';
import type { TradeReview } from '@/types/ai-review';
import { Button } from '@/components/ui/button';
import {
  Sparkles,
  Loader2,
  CheckCircle2,
  AlertTriangle,
  X,
  Award,
} from 'lucide-react';
import { cn } from '@/lib/utils';

export function TradeReviewButton({ tradeId }: { tradeId: string }) {
  const locale = useLocale() as 'ro' | 'en';
  const [open, setOpen] = useState(false);
  const [review, setReview] = useState<TradeReview | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const analyze = async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await fetch('/api/ai/review', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ tradeId, language: locale }),
      });
      if (!res.ok) {
        const err = await res.json();
        throw new Error(err.error || 'Review failed');
      }
      const data = await res.json();
      setReview(data.review);
      setOpen(true);
    } catch (err: unknown) {
      const e = err as { message?: string };
      setError(e.message || 'Unknown error');
    } finally {
      setLoading(false);
    }
  };

  return (
    <>
      <Button
        onClick={analyze}
        disabled={loading}
        size="sm"
        variant="outline"
        className="gap-1.5"
      >
        {loading ? (
          <Loader2 className="h-3 w-3 animate-spin" />
        ) : (
          <Sparkles className="h-3 w-3 text-primary" />
        )}
        {locale === 'ro' ? 'Analiză AI' : 'AI Review'}
      </Button>

      {error && (
        <p className="mt-1 text-xs text-destructive">{error}</p>
      )}

      {open && review && (
        <ReviewModal review={review} onClose={() => setOpen(false)} />
      )}
    </>
  );
}

function ReviewModal({
  review,
  onClose,
}: {
  review: TradeReview;
  onClose: () => void;
}) {
  const locale = useLocale() as 'ro' | 'en';

  const gradeColors: Record<string, string> = {
    A: 'bg-profit text-white',
    B: 'bg-primary text-primary-foreground',
    C: 'bg-yellow-500 text-white',
    D: 'bg-orange-500 text-white',
    F: 'bg-loss text-white',
  };

  const qualityLabels = {
    excellent: { ro: 'Excelent', en: 'Excellent', color: 'text-profit' },
    good: { ro: 'Bun', en: 'Good', color: 'text-primary' },
    neutral: { ro: 'Neutru', en: 'Neutral', color: 'text-muted-foreground' },
    poor: { ro: 'Slab', en: 'Poor', color: 'text-orange-500' },
    bad: { ro: 'Rău', en: 'Bad', color: 'text-loss' },
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center bg-black/60 p-4 backdrop-blur-sm overflow-y-auto"
      onClick={onClose}
    >
      <div
        className="my-8 w-full max-w-2xl rounded-xl border border-border bg-card p-6 shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="mb-6 flex items-start justify-between">
          <div className="flex items-center gap-4">
            <div
              className={cn(
                'flex h-16 w-16 items-center justify-center rounded-xl text-3xl font-bold',
                gradeColors[review.grade]
              )}
            >
              {review.grade}
            </div>
            <div>
              <h2 className="text-xl font-bold">
                {locale === 'ro' ? 'Analiză AI' : 'AI Review'}
              </h2>
              <p className="text-sm text-muted-foreground">
                {locale === 'ro' ? 'Scor' : 'Score'}: {review.score}/100
              </p>
            </div>
          </div>
          <Button variant="ghost" size="icon" onClick={onClose}>
            <X className="h-5 w-5" />
          </Button>
        </div>

        {/* Summary */}
        <div className="mb-6 rounded-lg border border-primary/30 bg-primary/5 p-4">
          <p className="text-sm leading-relaxed">{review.summary}</p>
        </div>

        {/* Quality indicators */}
        <div className="mb-6 grid grid-cols-3 gap-3">
          <QualityBox
            label={locale === 'ro' ? 'Intrare' : 'Entry'}
            quality={review.entryQuality}
            labels={qualityLabels}
            locale={locale}
          />
          <QualityBox
            label={locale === 'ro' ? 'Ieșire' : 'Exit'}
            quality={review.exitQuality}
            labels={qualityLabels}
            locale={locale}
          />
          <QualityBox
            label={locale === 'ro' ? 'Risc' : 'Risk'}
            quality={review.riskManagement}
            labels={qualityLabels}
            locale={locale}
          />
        </div>

        {/* Flags */}
        {review.flags.length > 0 && (
          <div className="mb-6">
            <p className="mb-2 text-xs font-semibold uppercase text-muted-foreground">
              {locale === 'ro' ? 'Pattern-uri detectate' : 'Detected patterns'}
            </p>
            <div className="flex flex-wrap gap-1.5">
              {review.flags.map((flag) => (
                <FlagBadge key={flag} flag={flag} locale={locale} />
              ))}
            </div>
          </div>
        )}

        {/* Strengths */}
        {review.strengths.length > 0 && (
          <div className="mb-4">
            <h3 className="mb-2 flex items-center gap-2 text-sm font-semibold">
              <CheckCircle2 className="h-4 w-4 text-profit" />
              {locale === 'ro' ? 'Puncte tari' : 'Strengths'}
            </h3>
            <ul className="space-y-1 text-sm">
              {review.strengths.map((s, i) => (
                <li key={i} className="flex gap-2">
                  <span className="text-profit">✓</span>
                  <span>{s}</span>
                </li>
              ))}
            </ul>
          </div>
        )}

        {/* Weaknesses */}
        {review.weaknesses.length > 0 && (
          <div className="mb-4">
            <h3 className="mb-2 flex items-center gap-2 text-sm font-semibold">
              <AlertTriangle className="h-4 w-4 text-orange-500" />
              {locale === 'ro' ? 'Puncte slabe' : 'Weaknesses'}
            </h3>
            <ul className="space-y-1 text-sm">
              {review.weaknesses.map((w, i) => (
                <li key={i} className="flex gap-2">
                  <span className="text-orange-500">!</span>
                  <span>{w}</span>
                </li>
              ))}
            </ul>
          </div>
        )}

        {/* Recommendations */}
        {review.recommendations.length > 0 && (
          <div className="rounded-lg border border-primary/30 bg-primary/5 p-4">
            <h3 className="mb-3 flex items-center gap-2 text-sm font-semibold text-primary">
              <Award className="h-4 w-4" />
              {locale === 'ro' ? 'Recomandări' : 'Recommendations'}
            </h3>
            <ol className="space-y-2 text-sm">
              {review.recommendations.map((r, i) => (
                <li key={i} className="flex gap-2">
                  <span className="font-semibold text-primary">{i + 1}.</span>
                  <span>{r}</span>
                </li>
              ))}
            </ol>
          </div>
        )}

        {/* Footer */}
        <div className="mt-6 flex items-center justify-between text-xs text-muted-foreground">
          <span>
            {locale === 'ro' ? 'Generat cu' : 'Generated with'} {review.provider}
          </span>
          <span>
            {new Date(review.createdAt).toLocaleString(
              locale === 'ro' ? 'ro-RO' : 'en-US'
            )}
          </span>
        </div>
      </div>
    </div>
  );
}

function QualityBox({
  label,
  quality,
  labels,
  locale,
}: {
  label: string;
  quality: string;
  labels: Record<string, { ro: string; en: string; color: string }>;
  locale: 'ro' | 'en';
}) {
  const info = labels[quality] || labels.neutral;
  return (
    <div className="rounded-lg border border-border bg-muted/20 p-3 text-center">
      <p className="text-xs text-muted-foreground">{label}</p>
      <p className={cn('mt-1 text-sm font-semibold', info.color)}>
        {info[locale]}
      </p>
    </div>
  );
}

function FlagBadge({ flag, locale }: { flag: string; locale: 'ro' | 'en' }) {
  const labels: Record<string, { ro: string; en: string; bad: boolean }> = {
    no_stop_loss: { ro: 'Fără Stop Loss', en: 'No Stop Loss', bad: true },
    oversized_position: { ro: 'Poziție prea mare', en: 'Oversized', bad: true },
    tight_stop: { ro: 'Stop prea apropiat', en: 'Tight Stop', bad: true },
    wide_stop: { ro: 'Stop prea larg', en: 'Wide Stop', bad: true },
    held_too_long: { ro: 'Ținut prea mult', en: 'Held Too Long', bad: true },
    cut_profits_early: { ro: 'Profit tăiat devreme', en: 'Cut Profits Early', bad: true },
    revenge_trade: { ro: 'Revenge Trade', en: 'Revenge Trade', bad: true },
    overtrading: { ro: 'Overtrading', en: 'Overtrading', bad: true },
    counter_trend: { ro: 'Contra trendului', en: 'Counter Trend', bad: true },
    chased_entry: { ro: 'Intrare forțată', en: 'Chased Entry', bad: true },
    good_rr_ratio: { ro: 'R/R bun', en: 'Good R/R', bad: false },
    disciplined_exit: { ro: 'Ieșire disciplinată', en: 'Disciplined Exit', bad: false },
    strong_setup: { ro: 'Setup solid', en: 'Strong Setup', bad: false },
  };
  const info = labels[flag];
  if (!info) return null;

  return (
    <span
      className={cn(
        'rounded-full border px-2.5 py-0.5 text-xs font-medium',
        info.bad
          ? 'border-loss/30 bg-loss/10 text-loss'
          : 'border-profit/30 bg-profit/10 text-profit'
      )}
    >
      {info[locale]}
    </span>
  );
}
