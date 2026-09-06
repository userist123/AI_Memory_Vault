import { setRequestLocale } from 'next-intl/server';
import { getTranslations } from 'next-intl/server';
import { Link } from '@/lib/i18n/routing';
import { Button } from '@/components/ui/button';
import { Header } from '@/components/layout/header';
import { Footer } from '@/components/layout/footer';
import {
  Mic,
  Brain,
  Radar,
  Plug,
  Receipt,
  BarChart3,
  ArrowRight,
  Check,
  Sparkles,
} from 'lucide-react';

export default async function LandingPage({
  params,
}: {
  params: Promise<{ locale: string }>;
}) {
  const { locale } = await params;
  setRequestLocale(locale);
  const t = await getTranslations('landing');
  const tCommon = await getTranslations('common');

  const features = [
    { key: 'voiceJournal', icon: Mic },
    { key: 'aiCoach', icon: Brain },
    { key: 'marketScanner', icon: Radar },
    { key: 'brokerSync', icon: Plug },
    { key: 'fiscalModule', icon: Receipt },
    { key: 'analytics', icon: BarChart3 },
  ] as const;

  const plans = ['free', 'pro', 'elite'] as const;
  const faqs = ['q1', 'q2', 'q3', 'q4', 'q5', 'q6'] as const;

  return (
    <>
      <Header />
      <main>
        {/* HERO */}
        <section className="relative overflow-hidden py-20 md:py-32">
          <div className="absolute inset-0 -z-10 bg-[radial-gradient(ellipse_at_top,_var(--tw-gradient-stops))] from-primary/10 via-background to-background" />
          <div className="container">
            <div className="mx-auto max-w-4xl text-center">
              <div className="mb-6 inline-flex items-center gap-2 rounded-full border border-primary/20 bg-primary/10 px-4 py-1.5 text-sm font-medium text-primary">
                <Sparkles className="h-3.5 w-3.5" />
                {t('hero.badge')}
              </div>
              <h1 className="text-4xl font-bold tracking-tight md:text-6xl lg:text-7xl">
                {t('hero.title')}
              </h1>
              <p className="mx-auto mt-6 max-w-2xl text-lg text-muted-foreground md:text-xl">
                {t('hero.subtitle')}
              </p>
              <div className="mt-10 flex flex-col items-center justify-center gap-4 sm:flex-row">
                <Link href="/signup">
                  <Button size="lg" className="gap-2">
                    {t('hero.ctaPrimary')}
                    <ArrowRight className="h-4 w-4" />
                  </Button>
                </Link>
                <Link href="#features">
                  <Button size="lg" variant="outline">
                    {t('hero.ctaSecondary')}
                  </Button>
                </Link>
              </div>
              <p className="mt-6 text-sm text-muted-foreground">
                {t('hero.noCreditCard')}
              </p>
            </div>
          </div>
        </section>

        {/* FEATURES */}
        <section id="features" className="py-20 md:py-32">
          <div className="container">
            <div className="mx-auto max-w-3xl text-center">
              <h2 className="text-3xl font-bold tracking-tight md:text-4xl">
                {t('features.title')}
              </h2>
              <p className="mt-4 text-lg text-muted-foreground">
                {t('features.subtitle')}
              </p>
            </div>

            <div className="mt-16 grid gap-6 md:grid-cols-2 lg:grid-cols-3">
              {features.map(({ key, icon: Icon }) => (
                <div
                  key={key}
                  className="group rounded-xl border border-border bg-card p-6 transition-all hover:border-primary/50 hover:shadow-lg"
                >
                  <div className="mb-4 inline-flex h-12 w-12 items-center justify-center rounded-lg bg-primary/10 text-primary transition-colors group-hover:bg-primary group-hover:text-primary-foreground">
                    <Icon className="h-6 w-6" />
                  </div>
                  <h3 className="text-lg font-semibold">
                    {t(`features.${key}.title`)}
                  </h3>
                  <p className="mt-2 text-sm text-muted-foreground">
                    {t(`features.${key}.description`)}
                  </p>
                </div>
              ))}
            </div>
          </div>
        </section>

        {/* PRICING */}
        <section id="pricing" className="bg-muted/30 py-20 md:py-32">
          <div className="container">
            <div className="mx-auto max-w-3xl text-center">
              <h2 className="text-3xl font-bold tracking-tight md:text-4xl">
                {t('pricing.title')}
              </h2>
              <p className="mt-4 text-lg text-muted-foreground">
                {t('pricing.subtitle')}
              </p>
            </div>

            <div className="mx-auto mt-16 grid max-w-5xl gap-6 md:grid-cols-3">
              {plans.map((plan) => {
                const isPro = plan === 'pro';
                const features = t.raw(`pricing.${plan}.features`) as string[];

                return (
                  <div
                    key={plan}
                    className={`relative rounded-xl border bg-card p-8 ${
                      isPro
                        ? 'border-primary shadow-lg shadow-primary/20'
                        : 'border-border'
                    }`}
                  >
                    {isPro && (
                      <div className="absolute -top-3 left-1/2 -translate-x-1/2 rounded-full bg-primary px-3 py-1 text-xs font-semibold text-primary-foreground">
                        {t('pricing.pro.badge')}
                      </div>
                    )}
                    <h3 className="text-xl font-bold">
                      {t(`pricing.${plan}.name`)}
                    </h3>
                    <p className="mt-2 text-sm text-muted-foreground">
                      {t(`pricing.${plan}.description`)}
                    </p>
                    <div className="mt-6 flex items-baseline gap-1">
                      <span className="text-4xl font-bold">
                        {plan === 'free'
                          ? locale === 'ro'
                            ? 'Gratuit'
                            : 'Free'
                          : `${locale === 'ro' ? '' : '$'}${t(`pricing.${plan}.price`)}`}
                      </span>
                      {plan !== 'free' && (
                        <span className="text-sm text-muted-foreground">
                          {t(`pricing.${plan}.period`)}
                        </span>
                      )}
                    </div>
                    {plan !== 'free' && (
                      <p className="mt-1 text-xs text-muted-foreground">
                        {t(`pricing.${plan}.yearlyPrice`)}
                      </p>
                    )}

                    <ul className="mt-6 space-y-3">
                      {features.map((feature, i) => (
                        <li key={i} className="flex items-start gap-2">
                          <Check className="mt-0.5 h-4 w-4 shrink-0 text-primary" />
                          <span className="text-sm">{feature}</span>
                        </li>
                      ))}
                    </ul>

                    <Link href="/signup" className="mt-8 block">
                      <Button
                        className="w-full"
                        variant={isPro ? 'default' : 'outline'}
                        size="lg"
                      >
                        {t(`pricing.${plan}.cta`)}
                      </Button>
                    </Link>
                  </div>
                );
              })}
            </div>
          </div>
        </section>

        {/* FAQ */}
        <section className="py-20 md:py-32">
          <div className="container">
            <div className="mx-auto max-w-3xl">
              <h2 className="text-center text-3xl font-bold tracking-tight md:text-4xl">
                {t('faq.title')}
              </h2>

              <div className="mt-12 space-y-4">
                {faqs.map((q, i) => (
                  <details
                    key={q}
                    className="group rounded-lg border border-border bg-card p-6 transition-colors hover:border-primary/50"
                    {...(i === 0 ? { open: true } : {})}
                  >
                    <summary className="cursor-pointer list-none">
                      <div className="flex items-center justify-between">
                        <h3 className="font-semibold">{t(`faq.${q}`)}</h3>
                        <ArrowRight className="h-4 w-4 transition-transform group-open:rotate-90" />
                      </div>
                    </summary>
                    <p className="mt-4 text-sm text-muted-foreground">
                      {t(`faq.${q.replace('q', 'a')}`)}
                    </p>
                  </details>
                ))}
              </div>
            </div>
          </div>
        </section>

        {/* CTA FINAL */}
        <section className="border-t border-border/40 bg-muted/30 py-20">
          <div className="container">
            <div className="mx-auto max-w-3xl text-center">
              <h2 className="text-3xl font-bold tracking-tight md:text-4xl">
                {locale === 'ro'
                  ? 'Gata să devii un trader mai bun?'
                  : 'Ready to become a better trader?'}
              </h2>
              <p className="mt-4 text-lg text-muted-foreground">
                {locale === 'ro'
                  ? 'Începe gratuit. Fără card. Fără angajamente.'
                  : 'Start free. No credit card. No commitments.'}
              </p>
              <Link href="/signup">
                <Button size="lg" className="mt-8 gap-2">
                  {t('hero.ctaPrimary')}
                  <ArrowRight className="h-4 w-4" />
                </Button>
              </Link>
            </div>
          </div>
        </section>
      </main>
      <Footer />
    </>
  );
}
