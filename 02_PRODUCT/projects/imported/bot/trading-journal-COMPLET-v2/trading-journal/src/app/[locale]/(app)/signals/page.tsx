import { setRequestLocale } from 'next-intl/server';
import { SignalsView } from '@/components/signals/signals-view';

export default async function SignalsPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  return (
    <div>
      <div className="mb-6">
        <h1 className="text-3xl font-bold tracking-tight">
          {locale === 'ro' ? 'Semnale trading' : 'Trading signals'}
        </h1>
        <p className="mt-1 text-sm text-muted-foreground">
          {locale === 'ro'
            ? 'Alerte automate cu setup-uri valide + execuție one-click. NU este sfat financiar.'
            : 'Automatic alerts with valid setups + one-click execution. NOT financial advice.'}
        </p>
      </div>
      <SignalsView />
    </div>
  );
}
