'use client';

import { useState, useRef, useCallback } from 'react';
import { useLocale } from 'next-intl';
import { Button } from '@/components/ui/button';
import {
  Upload,
  FileText,
  CheckCircle2,
  AlertCircle,
  Loader2,
  X,
  TrendingUp,
} from 'lucide-react';
import { cn } from '@/lib/utils';
import type { ImportResult } from '@/types/trade';

interface ImporterInfo {
  broker: string;
  displayName: string;
  fileTypes: string[];
  description: { ro: string; en: string };
}

const SUPPORTED_IMPORTERS: ImporterInfo[] = [
  {
    broker: 'mt5',
    displayName: 'MetaTrader 5',
    fileTypes: ['html', 'htm'],
    description: {
      ro: 'Raport HTML din MT5',
      en: 'HTML report from MT5',
    },
  },
  {
    broker: 'binance',
    displayName: 'Binance',
    fileTypes: ['csv'],
    description: {
      ro: 'Istoric spot CSV',
      en: 'Spot history CSV',
    },
  },
  {
    broker: 'trading212',
    displayName: 'Trading 212',
    fileTypes: ['csv'],
    description: {
      ro: 'Istoric tranzacții CSV',
      en: 'Transaction history CSV',
    },
  },
  {
    broker: 'xtb',
    displayName: 'XTB xStation',
    fileTypes: ['csv'],
    description: {
      ro: 'Statement CSV',
      en: 'Statement CSV',
    },
  },
  {
    broker: 'other',
    displayName: 'Generic CSV',
    fileTypes: ['csv', 'tsv'],
    description: {
      ro: 'Orice CSV cu coloane standard',
      en: 'Any CSV with standard columns',
    },
  },
];

export function TradeImporter({ onImportDone }: { onImportDone?: () => void }) {
  const locale = useLocale() as 'ro' | 'en';
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [isDragging, setIsDragging] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [selectedBroker, setSelectedBroker] = useState<string | null>(null);
  const [result, setResult] = useState<ImportResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const handleFile = useCallback(
    async (file: File) => {
      setUploading(true);
      setError(null);
      setResult(null);

      try {
        const formData = new FormData();
        formData.append('file', file);
        if (selectedBroker) {
          formData.append('broker', selectedBroker);
        }

        const res = await fetch('/api/trades/import', {
          method: 'POST',
          body: formData,
        });

        if (!res.ok) {
          const err = await res.json();
          throw new Error(err.error || 'Import failed');
        }

        const data: ImportResult = await res.json();
        setResult(data);
        onImportDone?.();
      } catch (err: unknown) {
        const e = err as { message?: string };
        setError(e.message || 'Unknown error');
      } finally {
        setUploading(false);
      }
    },
    [selectedBroker, onImportDone]
  );

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault();
    setIsDragging(false);
    const file = e.dataTransfer.files[0];
    if (file) handleFile(file);
  };

  const t = {
    title: locale === 'ro' ? 'Importă tranzacții' : 'Import Trades',
    subtitle:
      locale === 'ro'
        ? 'Încarcă fișierul de export de la brokerul tău. Detectăm automat formatul.'
        : 'Upload your broker export file. We auto-detect the format.',
    dropHere: locale === 'ro' ? 'Trage fișierul aici' : 'Drop file here',
    orClick: locale === 'ro' ? 'sau click pentru a selecta' : 'or click to select',
    selectBroker:
      locale === 'ro'
        ? 'Broker (opțional — auto-detectăm dacă nu selectezi)'
        : 'Broker (optional — we auto-detect if you skip)',
    autoDetect: locale === 'ro' ? 'Auto-detectare' : 'Auto-detect',
    uploading: locale === 'ro' ? 'Se încarcă...' : 'Uploading...',
    parsing: locale === 'ro' ? 'Se analizează fișierul...' : 'Parsing file...',
    importSuccess: locale === 'ro' ? 'Import reușit' : 'Import successful',
    importedTrades: locale === 'ro' ? 'Tranzacții importate' : 'Imported trades',
    duplicates: locale === 'ro' ? 'Duplicate (sărite)' : 'Duplicates (skipped)',
    errors: locale === 'ro' ? 'Erori' : 'Errors',
    detectedAs: locale === 'ro' ? 'Detectat ca' : 'Detected as',
    totalRows: locale === 'ro' ? 'Rânduri totale' : 'Total rows',
    importAnother: locale === 'ro' ? 'Importă alt fișier' : 'Import another file',
  };

  // Result view
  if (result) {
    const successful = result.importedTrades > 0;
    return (
      <div className="rounded-xl border border-border bg-card p-6">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-3">
            {successful ? (
              <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-profit/10 text-profit">
                <CheckCircle2 className="h-6 w-6" />
              </div>
            ) : (
              <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-loss/10 text-loss">
                <AlertCircle className="h-6 w-6" />
              </div>
            )}
            <div>
              <h3 className="font-semibold">{t.importSuccess}</h3>
              <p className="text-sm text-muted-foreground">
                {result.fileName} • {t.detectedAs}{' '}
                <span className="font-medium text-foreground">
                  {SUPPORTED_IMPORTERS.find((i) => i.broker === result.broker)?.displayName ||
                    result.broker}
                </span>
              </p>
            </div>
          </div>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => {
              setResult(null);
              setError(null);
            }}
          >
            {t.importAnother}
          </Button>
        </div>

        <div className="mt-6 grid grid-cols-2 gap-4 md:grid-cols-4">
          <StatBox label={t.totalRows} value={result.totalRows} />
          <StatBox
            label={t.importedTrades}
            value={result.importedTrades}
            valueClass="text-profit"
          />
          <StatBox
            label={t.duplicates}
            value={result.duplicates}
            valueClass="text-muted-foreground"
          />
          <StatBox
            label={t.errors}
            value={result.errors.length}
            valueClass={result.errors.length > 0 ? 'text-loss' : ''}
          />
        </div>

        {result.errors.length > 0 && (
          <details className="mt-6 rounded-lg border border-loss/30 bg-loss/5 p-4">
            <summary className="cursor-pointer text-sm font-medium text-loss">
              {locale === 'ro'
                ? `${result.errors.length} erori (click pentru detalii)`
                : `${result.errors.length} errors (click for details)`}
            </summary>
            <div className="mt-3 max-h-64 space-y-1 overflow-y-auto text-xs">
              {result.errors.slice(0, 20).map((err, i) => (
                <div key={i} className="text-muted-foreground">
                  <span className="font-mono">Row {err.row}:</span> {err.message}
                </div>
              ))}
              {result.errors.length > 20 && (
                <p className="pt-2 text-muted-foreground">
                  {locale === 'ro'
                    ? `... și ${result.errors.length - 20} mai multe`
                    : `... and ${result.errors.length - 20} more`}
                </p>
              )}
            </div>
          </details>
        )}

        {result.importedTrades > 0 && (
          <div className="mt-6 rounded-lg border border-primary/30 bg-primary/5 p-4 text-sm">
            <p className="font-medium text-primary">
              {locale === 'ro'
                ? `Mergi la secțiunea Tranzacții pentru a vedea toate cele ${result.importedTrades} tranzacții importate.`
                : `Go to Trades to view all ${result.importedTrades} imported trades.`}
            </p>
          </div>
        )}
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {/* Broker selector */}
      <div>
        <p className="mb-2 text-sm font-medium">{t.selectBroker}</p>
        <div className="flex flex-wrap gap-2">
          <button
            onClick={() => setSelectedBroker(null)}
            className={cn(
              'rounded-full border px-3 py-1.5 text-xs font-medium transition-colors',
              selectedBroker === null
                ? 'border-primary bg-primary text-primary-foreground'
                : 'border-border bg-background hover:border-primary/50'
            )}
          >
            {t.autoDetect}
          </button>
          {SUPPORTED_IMPORTERS.map((imp) => (
            <button
              key={imp.broker}
              onClick={() => setSelectedBroker(imp.broker)}
              className={cn(
                'rounded-full border px-3 py-1.5 text-xs font-medium transition-colors',
                selectedBroker === imp.broker
                  ? 'border-primary bg-primary text-primary-foreground'
                  : 'border-border bg-background hover:border-primary/50'
              )}
              title={imp.description[locale]}
            >
              {imp.displayName}
            </button>
          ))}
        </div>
      </div>

      {/* Drop zone */}
      <div
        onDragOver={(e) => {
          e.preventDefault();
          setIsDragging(true);
        }}
        onDragLeave={() => setIsDragging(false)}
        onDrop={handleDrop}
        onClick={() => fileInputRef.current?.click()}
        className={cn(
          'flex min-h-[240px] cursor-pointer flex-col items-center justify-center rounded-xl border-2 border-dashed transition-colors',
          isDragging
            ? 'border-primary bg-primary/5'
            : 'border-border bg-card hover:border-primary/50 hover:bg-primary/5',
          uploading && 'pointer-events-none opacity-50'
        )}
      >
        <input
          ref={fileInputRef}
          type="file"
          accept=".csv,.html,.htm,.tsv,.txt,.xlsx"
          className="hidden"
          onChange={(e) => {
            const file = e.target.files?.[0];
            if (file) handleFile(file);
          }}
        />

        {uploading ? (
          <>
            <Loader2 className="h-10 w-10 animate-spin text-primary" />
            <p className="mt-4 text-sm font-medium">{t.parsing}</p>
          </>
        ) : (
          <>
            <div className="flex h-16 w-16 items-center justify-center rounded-full bg-primary/10">
              <Upload className="h-8 w-8 text-primary" />
            </div>
            <p className="mt-4 text-base font-semibold">{t.dropHere}</p>
            <p className="mt-1 text-sm text-muted-foreground">{t.orClick}</p>
            <p className="mt-4 text-xs text-muted-foreground">CSV, HTML, TSV, XLSX (max 10 MB)</p>
          </>
        )}
      </div>

      {error && (
        <div className="flex items-start gap-3 rounded-lg border border-destructive/50 bg-destructive/10 p-4">
          <AlertCircle className="mt-0.5 h-5 w-5 shrink-0 text-destructive" />
          <div>
            <p className="font-medium text-destructive">
              {locale === 'ro' ? 'Eroare la import' : 'Import error'}
            </p>
            <p className="mt-1 text-sm text-muted-foreground">{error}</p>
          </div>
        </div>
      )}

      {/* Supported brokers hints */}
      <div className="rounded-lg border border-border bg-muted/30 p-4">
        <p className="mb-2 text-xs font-semibold uppercase text-muted-foreground">
          {locale === 'ro' ? 'Brokeri suportați' : 'Supported brokers'}
        </p>
        <div className="grid gap-2 text-xs md:grid-cols-2">
          {SUPPORTED_IMPORTERS.filter((i) => i.broker !== 'other').map((imp) => (
            <div key={imp.broker} className="flex items-start gap-2">
              <FileText className="mt-0.5 h-3 w-3 shrink-0 text-primary" />
              <div>
                <span className="font-medium">{imp.displayName}:</span>{' '}
                <span className="text-muted-foreground">{imp.description[locale]}</span>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

function StatBox({
  label,
  value,
  valueClass,
}: {
  label: string;
  value: number;
  valueClass?: string;
}) {
  return (
    <div className="rounded-lg border border-border bg-background p-3">
      <p className="text-xs text-muted-foreground">{label}</p>
      <p className={cn('mt-1 text-2xl font-bold', valueClass)}>{value}</p>
    </div>
  );
}
