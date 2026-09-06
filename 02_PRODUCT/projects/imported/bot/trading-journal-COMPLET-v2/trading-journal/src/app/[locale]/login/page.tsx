import { setRequestLocale } from 'next-intl/server';
import { getTranslations } from 'next-intl/server';
import { redirect } from '@/lib/i18n/routing';
import { Link } from '@/lib/i18n/routing';
import { LanguageSwitcher } from '@/components/layout/language-switcher';
import { LoginForm } from '@/components/auth/login-form';
import { getCurrentUser } from '@/lib/auth/session';
import { TrendingUp } from 'lucide-react';

export default async function LoginPage({
  params,
}: {
  params: Promise<{ locale: string }>;
}) {
  const { locale } = await params;
  setRequestLocale(locale);

  // If already logged in, redirect to dashboard
  const user = await getCurrentUser();
  if (user) {
    redirect({ href: '/dashboard', locale });
  }

  const t = await getTranslations('auth');

  return (
    <div className="flex min-h-screen items-center justify-center bg-background p-4">
      <div className="absolute right-4 top-4">
        <LanguageSwitcher />
      </div>

      <div className="w-full max-w-md">
        <Link href="/" className="mb-8 flex items-center justify-center gap-2">
          <div className="flex h-10 w-10 items-center justify-center rounded-md bg-primary">
            <TrendingUp className="h-6 w-6 text-primary-foreground" />
          </div>
          <span className="text-xl font-bold">Trading Journal</span>
        </Link>

        <div className="rounded-xl border border-border bg-card p-8">
          <h1 className="text-2xl font-bold">{t('welcomeBack')}</h1>
          <p className="mt-2 text-sm text-muted-foreground">{t('signIn')}</p>

          <div className="mt-6">
            <LoginForm />
          </div>
        </div>
      </div>
    </div>
  );
}
