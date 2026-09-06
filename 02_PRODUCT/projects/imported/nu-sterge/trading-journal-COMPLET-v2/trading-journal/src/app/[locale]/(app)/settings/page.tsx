import { setRequestLocale } from 'next-intl/server';
import { getTranslations } from 'next-intl/server';

export default async function Page({
  params,
}: {
  params: Promise<{ locale: string }>;
}) {
  const { locale } = await params;
  setRequestLocale(locale);
  const t = await getTranslations('nav');

  return (
    <div>
      <h1 className="text-3xl font-bold tracking-tight">{t('settings')}</h1>
      <div className="mt-8 rounded-xl border border-dashed border-border p-12 text-center">
        <p className="text-muted-foreground">
          {locale === 'ro' ? 'Se implementează în pasul următor.' : 'Implemented in the next step.'}
        </p>
      </div>
    </div>
  );
}
