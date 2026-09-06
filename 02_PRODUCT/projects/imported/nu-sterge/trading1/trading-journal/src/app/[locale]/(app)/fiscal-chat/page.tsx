import { setRequestLocale } from 'next-intl/server';
import { FiscalChatView } from '@/components/fiscal/fiscal-chat-view';

export default async function FiscalChatPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  return (
    <div>
      <div className="mb-6">
        <h1 className="text-3xl font-bold tracking-tight">
          {locale === 'ro' ? 'Întreabă consultantul fiscal' : 'Ask fiscal advisor'}
        </h1>
        <p className="mt-1 text-sm text-muted-foreground">
          {locale === 'ro'
            ? 'Chat AI specializat pe impozitare trading România. Cunoaște datele tale.'
            : 'AI chat specialized in Romanian trading taxation. Knows your data.'}
        </p>
      </div>
      <FiscalChatView />
    </div>
  );
}
