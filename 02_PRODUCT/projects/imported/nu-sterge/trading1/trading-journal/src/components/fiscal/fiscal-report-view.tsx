'use client';

import { useEffect, useState } from 'react';
import { useLocale } from 'next-intl';
import { Link } from '@/lib/i18n/routing';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import type { FiscalReport } from '@/lib/fiscal/types';
import {
  FileText,
  Download,
  Loader2,
  AlertTriangle,
  Info,
  Lock,
  Sparkles,
  TrendingUp,
  TrendingDown,
  Calendar,
} from 'lucide-react';

export function FiscalReportView() {
  const locale = useLocale() as 'ro' | 'en';
  const currentYear = new Date().getFullYear();
  const [year, setYear] = useState(currentYear - 1);
  const [report, setReport] = useState<FiscalReport | null>(null);
  const [hasFullAccess, setHasFullAccess] = useState(true);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);

    fetch(`/api/fiscal/report?year=${year}`)
      .then((r) => r.json())
      .then((data) => {
        if (cancelled) return;
        if (data.error) {
          setError(data.details || data.error);
          setReport(null);
        } else {
          setReport(data.report);
          setHasFullAccess(data.hasFullAccess ?? true);
        }
      })
      .catch((err) => {
        if (!cancelled) setError(err.message);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [year]);

  const handleExport = (format: 'csv' | 'd212') => {
    window.location.href = `/api/fiscal/export?year=${year}&format=${format}`;
  };

  const formatRon = (value: number) =>
    new Intl.NumberFormat(locale === 'ro' ? 'ro-RO' : 'en-US', {
      style: 'currency',
      currency: 'RON',
      minimumFractionDigits: 2,
    }).format(value);

  return (
    <div className="space-y-6">
      {/* Year selector */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <Calendar className="h-4 w-4 text-muted-foreground" />
          <span className="text-sm font-medium">{locale === 'ro' ? 'An fiscal:' : 'Fiscal year:'}</span>
          <select
            value={year}
            onChange={(e) => setYear(parseInt(e.target.value))}
            className="rounded-md border border-input bg-background px-3 py-1.5 text-sm"
          >
            {[currentYear, currentYear - 1, currentYear - 2, currentYear - 3].map((y) => (
              <option key={y} value={y}>{y}</option>
            ))}
          </select>
        </div>

        {report && hasFullAccess && report.totalIncomeTaxDue > 0 && (
          <div className="flex gap-2">
            <Button onClick={() => handleExport('csv')} size="sm" variant="outline" className="gap-2">
              <Download className="h-4 w-4" />
              {locale === 'ro' ? 'CSV detaliat' : 'Detailed CSV'}
            </Button>
            <Button onClick={() => handleExport('d212')} size="sm" className="gap-2">
              <FileText className="h-4 w-4" />
              {locale === 'ro' ? 'Rezumat D212' : 'D212 Summary'}
            </Button>
          </div>
        )}
      </div>

      {loading && (
        <div className="flex h-48 items-center justify-center rounded-xl border border-border bg-card">
          <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
        </div>
      )}

      {error && (
        <div className="rounded-lg border border-destructive/50 bg-destructive/10 p-4 text-sm">
          {error}
        </div>
      )}

      {report && !loading && (
        <>
          {!hasFullAccess && <UpgradeBanner locale={locale} />}

          {/* Totals cards */}
          <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
            <Stat
              label={locale === 'ro' ? 'Câștiguri brute' : 'Gross gains'}
              value={formatRon(report.totalGainsRon)}
              valueClass="text-profit"
              icon={TrendingUp}
            />
            <Stat
              label={locale === 'ro' ? 'Pierderi' : 'Losses'}
              value={formatRon(-report.totalLossesRon)}
              valueClass="text-loss"
              icon={TrendingDown}
            />
            <Stat
              label={locale === 'ro' ? 'Venit declarabil' : 'Declarable income'}
              value={formatRon(report.netDeclarableIncomeRon)}
            />
            <Stat
              label={locale === 'ro' ? 'Total de plată' : 'Total due'}
              value={formatRon(report.totalDueStandard)}
              valueClass={report.totalDueStandard > 0 ? 'text-loss' : 'text-muted-foreground'}
            />
          </div>

          {/* Tax breakdown */}
          <div className="rounded-xl border border-border bg-card p-6">
            <h3 className="mb-4 font-semibold">
              {locale === 'ro' ? 'Defalcare impozite' : 'Tax breakdown'}
            </h3>
            <div className="space-y-3">
              <TaxRow
                label={`${locale === 'ro' ? 'Impozit crypto' : 'Crypto tax'} (${(report.params.cryptoTaxRate * 100).toFixed(0)}%)`}
                value={report.cryptoTaxDue}
              />
              <TaxRow
                label={`${locale === 'ro' ? 'Impozit câștiguri capital' : 'Capital gains tax'} (${(report.params.capitalGainsTaxRate * 100).toFixed(0)}%)`}
                value={report.capitalGainsTaxDue}
              />
              <TaxRow
                label={`CASS (${locale === 'ro' ? 'prag' : 'threshold'} ${report.cassThresholdReached} ${locale === 'ro' ? 'salarii' : 'wages'})`}
                value={report.cassDue}
              />
              <div className="border-t border-border pt-3">
                <TaxRow
                  label={locale === 'ro' ? 'Total standard' : 'Standard total'}
                  value={report.totalDueStandard}
                  bold
                />
              </div>
              {report.bonificationApplicable && report.bonificationAmount > 0 && (
                <div className="rounded-lg border border-profit/30 bg-profit/5 p-3">
                  <div className="flex items-center justify-between text-sm">
                    <span className="font-medium text-profit">
                      💰 {locale === 'ro' ? 'Cu bonificație 3%' : 'With 3% bonus'}
                      <span className="ml-2 text-xs text-muted-foreground">
                        ({locale === 'ro' ? 'plătești până la' : 'pay by'} {report.params.bonificationDeadline})
                      </span>
                    </span>
                    <span className="font-bold text-profit">
                      {formatRon(report.totalDueIfBonificationApplied)}
                    </span>
                  </div>
                  <p className="mt-1 text-xs text-muted-foreground">
                    {locale === 'ro' ? 'Economisești' : 'You save'}: {formatRon(report.bonificationAmount)}
                  </p>
                </div>
              )}
            </div>
          </div>

          {/* Per-category detail */}
          <div className="rounded-xl border border-border bg-card p-6">
            <h3 className="mb-4 font-semibold">
              {locale === 'ro' ? 'Detaliu pe categorii' : 'Breakdown by category'}
            </h3>
            <div className="space-y-3">
              {Object.entries(report.categories)
                .filter(([, cat]) => cat.tradeCount > 0)
                .map(([key, cat]) => (
                  <CategoryRow key={key} categoryKey={key} cat={cat} locale={locale} formatRon={formatRon} />
                ))}
              {Object.values(report.categories).every((c) => c.tradeCount === 0) && (
                <p className="text-sm text-muted-foreground">
                  {locale === 'ro'
                    ? `Nu ai tranzacții închise în anul ${report.year}.`
                    : `No closed trades in ${report.year}.`}
                </p>
              )}
            </div>
          </div>

          {/* Notes */}
          {report.notes.length > 0 && (
            <div className="rounded-xl border border-primary/30 bg-primary/5 p-4">
              <div className="mb-2 flex items-center gap-2 text-sm font-semibold text-primary">
                <Info className="h-4 w-4" />
                {locale === 'ro' ? 'Note importante' : 'Important notes'}
              </div>
              <ul className="space-y-1.5 text-sm">
                {report.notes.map((n, i) => (
                  <li key={i} className="flex gap-2">
                    <span className="text-primary">•</span>
                    <span>{n}</span>
                  </li>
                ))}
              </ul>
            </div>
          )}

          {report.warnings.length > 0 && (
            <div className="rounded-xl border border-orange-500/30 bg-orange-500/5 p-4">
              <div className="mb-2 flex items-center gap-2 text-sm font-semibold text-orange-500">
                <AlertTriangle className="h-4 w-4" />
                {locale === 'ro' ? 'Avertizări' : 'Warnings'}
              </div>
              <ul className="space-y-1 text-xs">
                {report.warnings.slice(0, 10).map((w, i) => (
                  <li key={i} className="text-muted-foreground">{w}</li>
                ))}
              </ul>
            </div>
          )}

          <div className="rounded-lg border border-border bg-muted/30 p-4 text-xs text-muted-foreground">
            <strong>Disclaimer:</strong>{' '}
            {locale === 'ro'
              ? 'Acest raport e orientativ. Consultă un expert contabil pentru depunerea Declarației Unice (Formular 212) la ANAF. Regulile fiscale pot varia în funcție de situația personală.'
              : 'This report is informational. Consult a certified accountant for filing the Single Declaration (Form 212) with ANAF.'}
          </div>
        </>
      )}
    </div>
  );
}

function Stat({
  label,
  value,
  valueClass,
  icon: Icon,
}: {
  label: string;
  value: string;
  valueClass?: string;
  icon?: React.ComponentType<{ className?: string }>;
}) {
  return (
    <div className="rounded-lg border border-border bg-card p-4">
      <div className="flex items-center justify-between">
        <p className="text-xs text-muted-foreground">{label}</p>
        {Icon && <Icon className="h-3 w-3 text-muted-foreground" />}
      </div>
      <p className={cn('mt-1 text-lg font-bold font-mono', valueClass)}>{value}</p>
    </div>
  );
}

function TaxRow({
  label,
  value,
  bold,
}: {
  label: string;
  value: number;
  bold?: boolean;
}) {
  const formatted = new Intl.NumberFormat('ro-RO', {
    style: 'currency',
    currency: 'RON',
    minimumFractionDigits: 2,
  }).format(value);
  return (
    <div className="flex items-center justify-between text-sm">
      <span className={cn(bold && 'font-semibold')}>{label}</span>
      <span className={cn('font-mono', bold && 'font-bold text-lg')}>{formatted}</span>
    </div>
  );
}

function CategoryRow({
  categoryKey,
  cat,
  locale,
  formatRon,
}: {
  categoryKey: string;
  cat: FiscalReport['categories'][keyof FiscalReport['categories']];
  locale: 'ro' | 'en';
  formatRon: (v: number) => string;
}) {
  const labels: Record<string, { ro: string; en: string }> = {
    crypto: { ro: 'Criptomonede', en: 'Crypto' },
    stocks_eu: { ro: 'Acțiuni/ETF (broker nerezident)', en: 'Stocks/ETF (non-resident broker)' },
    stocks_ro: { ro: 'Acțiuni (broker rezident RO)', en: 'Stocks (RO resident broker)' },
    forex: { ro: 'Forex', en: 'Forex' },
    other: { ro: 'Alte', en: 'Other' },
  };
  const label = labels[categoryKey]?.[locale] || categoryKey;

  const isStocksRo = categoryKey === 'stocks_ro';

  return (
    <div className="rounded-lg border border-border/40 bg-muted/20 p-3">
      <div className="mb-2 flex items-center justify-between">
        <span className="font-medium text-sm">{label}</span>
        <span className="text-xs text-muted-foreground">
          {cat.tradeCount} {locale === 'ro' ? 'tranzacții' : 'trades'}
        </span>
      </div>
      <div className="grid grid-cols-2 gap-2 text-xs md:grid-cols-4">
        <div>
          <p className="text-muted-foreground">{locale === 'ro' ? 'Câștiguri' : 'Gains'}</p>
          <p className="font-mono font-medium text-profit">{formatRon(cat.grossGainsRon)}</p>
        </div>
        <div>
          <p className="text-muted-foreground">{locale === 'ro' ? 'Pierderi' : 'Losses'}</p>
          <p className="font-mono font-medium text-loss">{formatRon(-cat.grossLossesRon)}</p>
        </div>
        <div>
          <p className="text-muted-foreground">{locale === 'ro' ? 'Declarabil' : 'Declarable'}</p>
          <p className="font-mono font-medium">{formatRon(cat.declarableIncomeRon)}</p>
        </div>
        <div>
          <p className="text-muted-foreground">
            {locale === 'ro' ? 'Impozit' : 'Tax'} ({(cat.taxRate * 100).toFixed(0)}%)
          </p>
          <p className="font-mono font-bold">
            {isStocksRo
              ? (locale === 'ro' ? 'la sursă' : 'at source')
              : formatRon(cat.taxDue)}
          </p>
        </div>
      </div>
    </div>
  );
}

function UpgradeBanner({ locale }: { locale: 'ro' | 'en' }) {
  return (
    <div className="rounded-xl border-2 border-dashed border-primary/50 bg-primary/5 p-6">
      <div className="flex items-start gap-4">
        <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-lg bg-primary/20">
          <Lock className="h-6 w-6 text-primary" />
        </div>
        <div className="flex-1">
          <h3 className="font-bold">
            {locale === 'ro' ? 'Modul fiscal complet - Pro/Elite' : 'Full fiscal module - Pro/Elite'}
          </h3>
          <p className="mt-1 text-sm text-muted-foreground">
            {locale === 'ro'
              ? 'Plan Free: preview cu totaluri. Upgrade la Pro pentru export CSV complet, rezumat D212 gata de completat, și detaliu per-tranzacție cu rate BNR.'
              : 'Free plan: preview with totals only. Upgrade to Pro for full CSV export, D212 ready-to-fill summary, and per-trade detail with BNR rates.'}
          </p>
          <Link href="/pricing">
            <Button size="sm" className="mt-3 gap-2">
              <Sparkles className="h-4 w-4" />
              {locale === 'ro' ? 'Vezi planurile' : 'See plans'}
            </Button>
          </Link>
        </div>
      </div>
    </div>
  );
}
