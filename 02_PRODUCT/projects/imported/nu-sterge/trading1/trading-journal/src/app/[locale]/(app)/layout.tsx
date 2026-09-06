import { setRequestLocale } from 'next-intl/server';
import { redirect } from '@/lib/i18n/routing';
import { getCurrentUser } from '@/lib/auth/session';
import { AppSidebar } from '@/components/layout/app-sidebar';

export default async function AppLayout({
  children,
  params,
}: {
  children: React.ReactNode;
  params: Promise<{ locale: string }>;
}) {
  const { locale } = await params;
  setRequestLocale(locale);

  // Protect all (app) routes - redirect unauthenticated users
  const user = await getCurrentUser();
  if (!user) {
    redirect({ href: '/login', locale });
  }

  return (
    <div className="flex min-h-screen">
      <AppSidebar user={user} />
      <main className="flex-1 overflow-y-auto">
        <div className="container py-8">{children}</div>
      </main>
    </div>
  );
}
