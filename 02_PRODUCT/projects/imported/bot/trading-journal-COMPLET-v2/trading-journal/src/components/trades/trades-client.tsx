'use client';

import { useState } from 'react';
import { useLocale, useTranslations } from 'next-intl';
import { TradeImporter } from '@/components/trades/trade-importer';
import { TradesTable } from '@/components/trades/trades-table';
import { Button } from '@/components/ui/button';
import { Upload, Table } from 'lucide-react';

export function TradesClient() {
  const locale = useLocale() as 'ro' | 'en';
  const t = useTranslations('trades');
  const [view, setView] = useState<'table' | 'import'>('table');
  const [refreshKey, setRefreshKey] = useState(0);

  return (
    <div>
      <div className="mb-6 flex items-center justify-between">
        <div>
          <h1 className="text-3xl font-bold tracking-tight">{t('title')}</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            {locale === 'ro'
              ? 'Vezi toate tranzacțiile, importă din broker, analizează performanța.'
              : 'View all trades, import from broker, analyze performance.'}
          </p>
        </div>
        <div className="flex gap-2">
          <Button
            variant={view === 'table' ? 'default' : 'outline'}
            size="sm"
            onClick={() => setView('table')}
            className="gap-2"
          >
            <Table className="h-4 w-4" />
            {locale === 'ro' ? 'Tabel' : 'Table'}
          </Button>
          <Button
            variant={view === 'import' ? 'default' : 'outline'}
            size="sm"
            onClick={() => setView('import')}
            className="gap-2"
          >
            <Upload className="h-4 w-4" />
            {t('importTrades')}
          </Button>
        </div>
      </div>

      {view === 'import' ? (
        <TradeImporter onImportDone={() => setRefreshKey((k) => k + 1)} />
      ) : (
        <TradesTable refreshKey={refreshKey} />
      )}
    </div>
  );
}
