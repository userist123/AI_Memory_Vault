import type { Metadata } from 'next';
import { notFound } from 'next/navigation';
import { NextIntlClientProvider } from 'next-intl';
import { getMessages, setRequestLocale } from 'next-intl/server';
import { routing } from '@/lib/i18n/routing';
import { ThemeProvider } from '@/components/theme-provider';
import '@/app/globals.css';

export function generateStaticParams() {
  return routing.locales.map((locale) => ({ locale }));
}

export async function generateMetadata({
  params,
}: {
  params: Promise<{ locale: string }>;
}): Promise<Metadata> {
  const { locale } = await params;
  const messages = (await import(`../../../public/locales/${locale}/common.json`))
    .default;

  const isRo = locale === 'ro';

  return {
    title: {
      template: `%s | ${messages.app.name}`,
      default: messages.app.name,
    },
    description: messages.app.description,
    keywords: isRo
      ? [
          'jurnal trading',
          'jurnal tranzacționare',
          'jurnal de trading România',
          'analiză trading AI',
          'trading România',
          'declarația unică ANAF trading',
          'jurnal forex România',
          'XTB jurnal',
          'Trading 212 jurnal',
        ]
      : [
          'trading journal',
          'AI trading journal',
          'voice journal trading',
          'trading analytics',
          'forex journal',
          'trade tracker',
          'trading psychology',
        ],
    authors: [{ name: 'Trading Journal' }],
    openGraph: {
      title: messages.app.name,
      description: messages.app.description,
      locale: locale === 'ro' ? 'ro_RO' : 'en_US',
      type: 'website',
      siteName: messages.app.name,
    },
    twitter: {
      card: 'summary_large_image',
      title: messages.app.name,
      description: messages.app.description,
    },
    alternates: {
      languages: {
        ro: '/ro',
        en: '/en',
        'x-default': '/ro',
      },
    },
    robots: {
      index: true,
      follow: true,
    },
  };
}

export default async function LocaleLayout({
  children,
  params,
}: {
  children: React.ReactNode;
  params: Promise<{ locale: string }>;
}) {
  const { locale } = await params;

  if (!routing.locales.includes(locale as 'ro' | 'en')) {
    notFound();
  }

  setRequestLocale(locale);
  const messages = await getMessages();

  return (
    <html lang={locale} suppressHydrationWarning>
      <body className="min-h-screen bg-background font-sans antialiased">
        <NextIntlClientProvider messages={messages}>
          <ThemeProvider
            attribute="class"
            defaultTheme="dark"
            enableSystem
            disableTransitionOnChange
          >
            {children}
          </ThemeProvider>
        </NextIntlClientProvider>
      </body>
    </html>
  );
}
