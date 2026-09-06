import { setRequestLocale } from 'next-intl/server';
import { Header } from '@/components/layout/header';
import { Footer } from '@/components/layout/footer';
import { PricingClient } from '@/components/billing/pricing-client';
import { getCurrentUser } from '@/lib/auth/session';

export default async function PricingPage({
  params,
}: {
  params: Promise<{ locale: string }>;
}) {
  const { locale } = await params;
  setRequestLocale(locale);

  const user = await getCurrentUser();

  return (
    <>
      <Header />
      <main className="container py-16">
        <div className="mb-12 text-center">
          <h1 className="text-4xl font-bold tracking-tight md:text-5xl">
            {locale === 'ro' ? 'Prețuri simple, fără surprize' : 'Simple pricing, no surprises'}
          </h1>
          <p className="mx-auto mt-4 max-w-2xl text-lg text-muted-foreground">
            {locale === 'ro'
              ? 'Începi gratis cu toate feature-urile esențiale. Upgrade când ai nevoie de nelimitat.'
              : 'Start free with all essential features. Upgrade when you need unlimited.'}
          </p>
        </div>

        <PricingClient
          currentPlan={user?.plan}
          isDev={process.env.NODE_ENV !== 'production'}
        />
      </main>
      <Footer />
    </>
  );
}
