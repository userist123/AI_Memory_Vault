import { setRequestLocale } from 'next-intl/server';
import { ConsultingView } from '@/components/consulting/consulting-view';

export default async function AdminTicketsPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  return (
    <div>
      <div className="mb-6">
        <h1 className="text-3xl font-bold tracking-tight">
          {locale === 'ro' ? 'Admin · Tickete' : 'Admin · Tickets'}
        </h1>
        <p className="mt-1 text-sm text-muted-foreground">
          {locale === 'ro' ? 'Vezi și răspunde la ticketele clienților.' : 'View and reply to customer tickets.'}
        </p>
      </div>
      <ConsultingView adminMode />
    </div>
  );
}
