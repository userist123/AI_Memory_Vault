import { setRequestLocale } from 'next-intl/server';
import { ConsultingView } from '@/components/consulting/consulting-view';

export default async function ConsultingPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  return (
    <div>
      <div className="mb-6">
        <h1 className="text-3xl font-bold tracking-tight">
          {locale === 'ro' ? 'Consultanță 1-la-1' : '1-on-1 consulting'}
        </h1>
        <p className="mt-1 text-sm text-muted-foreground">
          {locale === 'ro'
            ? 'Întrebări complexe care necesită expertiză umană. Răspuns în 24h.'
            : 'Complex questions requiring human expertise. Response in 24h.'}
        </p>
      </div>
      <ConsultingView />
    </div>
  );
}
