import { setRequestLocale } from 'next-intl/server';
import { getTranslations } from 'next-intl/server';
import { WeeklyCoach } from '@/components/ai/weekly-coach';

export default async function AnalyticsPage({
  params,
}: {
  params: Promise<{ locale: string }>;
}) {
  const { locale } = await params;
  setRequestLocale(locale);
  const t = await getTranslations('nav');

  return (
    <div>
      <div className="mb-6">
        <h1 className="text-3xl font-bold tracking-tight">{t('analytics')}</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          {locale === 'ro'
            ? 'Analiză profundă AI a performanței tale. Pattern-uri, puncte slabe, plan de acțiune.'
            : 'Deep AI analysis of your performance. Patterns, weaknesses, action plan.'}
        </p>
      </div>

      <WeeklyCoach />
    </div>
  );
}
