import { setRequestLocale } from 'next-intl/server';
import { TradesClient } from '@/components/trades/trades-client';

export default async function TradesPage({
  params,
}: {
  params: Promise<{ locale: string }>;
}) {
  const { locale } = await params;
  setRequestLocale(locale);

  return <TradesClient />;
}
