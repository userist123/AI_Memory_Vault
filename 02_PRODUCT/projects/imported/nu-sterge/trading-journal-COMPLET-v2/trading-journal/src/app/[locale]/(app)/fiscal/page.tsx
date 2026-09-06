import { setRequestLocale } from 'next-intl/server';
import { FiscalReportView } from '@/components/fiscal/fiscal-report-view';

export default async function FiscalPage({
  params,
}: {
  params: Promise<{ locale: string }>;
}) {
  const { locale } = await params;
  setRequestLocale(locale);

  return (
    <div>
      <div className="mb-8">
        <h1 className="text-3xl font-bold tracking-tight">
          {locale === 'ro' ? 'Modul fiscal România' : 'Romania fiscal module'}
        </h1>
        <p className="mt-1 text-sm text-muted-foreground">
          {locale === 'ro'
            ? 'Calcul automat impozit crypto 16%, câștiguri capital 10%, CASS, export Declarația Unică.'
            : 'Automatic calculation of crypto 16%, capital gains 10%, CASS, and D212 export.'}
        </p>
      </div>

      <FiscalReportView />
    </div>
  );
}
