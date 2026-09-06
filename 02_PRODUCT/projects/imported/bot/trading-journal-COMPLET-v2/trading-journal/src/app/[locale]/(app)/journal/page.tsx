import { setRequestLocale } from 'next-intl/server';
import { getTranslations } from 'next-intl/server';
import { VoiceJournal } from '@/components/journal/voice-journal';
import { JournalEntriesList } from '@/components/journal/journal-entries-list';

export default async function JournalPage({
  params,
}: {
  params: Promise<{ locale: string }>;
}) {
  const { locale } = await params;
  setRequestLocale(locale);
  const t = await getTranslations('journal');

  return (
    <div>
      <div className="mb-6">
        <h1 className="text-3xl font-bold tracking-tight">{t('title')}</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          {locale === 'ro'
            ? 'Înregistrează după fiecare tranzacție. AI-ul extrage automat setup, emoții, greșeli, lecții.'
            : 'Record after each trade. AI automatically extracts setup, emotions, mistakes, lessons.'}
        </p>
      </div>

      <VoiceJournal />

      <div className="mt-12">
        <h2 className="mb-4 text-xl font-semibold">
          {locale === 'ro' ? 'Intrări recente' : 'Recent entries'}
        </h2>
        <JournalEntriesList />
      </div>
    </div>
  );
}
