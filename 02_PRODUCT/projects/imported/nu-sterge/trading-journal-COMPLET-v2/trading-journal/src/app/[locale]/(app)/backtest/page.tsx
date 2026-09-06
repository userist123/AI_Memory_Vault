import { setRequestLocale } from 'next-intl/server';
import { BacktestView } from '@/components/backtest/backtest-view';

export default async function BacktestPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  return (
    <div>
      <div className="mb-6">
        <h1 className="text-3xl font-bold tracking-tight">
          {locale === 'ro' ? 'Backtester strategie' : 'Strategy backtester'}
        </h1>
        <p className="mt-1 text-sm text-muted-foreground">
          {locale === 'ro'
            ? 'Testează strategiile pe date istorice înainte să riști bani reali.'
            : 'Test strategies on historical data before risking real money.'}
        </p>
      </div>
      <BacktestView />
    </div>
  );
}
