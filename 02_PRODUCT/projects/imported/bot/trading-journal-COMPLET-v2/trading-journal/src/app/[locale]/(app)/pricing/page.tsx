import { setRequestLocale } from 'next-intl/server';
import { PricingClient } from '@/components/billing/pricing-client';
import { getCurrentUser } from '@/lib/auth/session';

export default async function AppPricingPage({
  params,
}: {
  params: Promise<{ locale: string }>;
}) {
  const { locale } = await params;
  setRequestLocale(locale);
  const user = await getCurrentUser();

  return (
    <div>
      <div className="mb-8">
        <h1 className="text-3xl font-bold tracking-tight">
          {locale === 'ro' ? 'Planuri și upgrade' : 'Plans & upgrade'}
        </h1>
        <p className="mt-1 text-sm text-muted-foreground">
          {locale === 'ro'
            ? 'Treci la Pro sau Elite pentru nelimitat și features avansate.'
            : 'Upgrade to Pro or Elite for unlimited and advanced features.'}
        </p>
      </div>

      <PricingClient
        currentPlan={user?.plan || 'free'}
        isDev={process.env.NODE_ENV !== 'production'}
      />
    </div>
  );
}
