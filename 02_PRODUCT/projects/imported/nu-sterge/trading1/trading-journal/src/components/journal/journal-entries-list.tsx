'use client';

import { useEffect, useState } from 'react';
import { useLocale, useTranslations } from 'next-intl';
import type { JournalEntry } from '@/types/journal';
import { formatDate } from '@/lib/utils';
import { BookOpen, Loader2 } from 'lucide-react';

export function JournalEntriesList() {
  const locale = useLocale() as 'ro' | 'en';
  const t = useTranslations('journal');
  const [entries, setEntries] = useState<JournalEntry[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let mounted = true;

    const loadEntries = async () => {
      try {
        const res = await fetch('/api/journal/entries');
        if (!res.ok) return;
        const data = await res.json();
        if (mounted) {
          setEntries(data.entries || []);
        }
      } catch (err) {
        console.error('[Entries] Load error:', err);
      } finally {
        if (mounted) setLoading(false);
      }
    };

    loadEntries();
    // Refresh every 5s to catch new entries
    const interval = setInterval(loadEntries, 5000);

    return () => {
      mounted = false;
      clearInterval(interval);
    };
  }, []);

  if (loading) {
    return (
      <div className="flex h-48 items-center justify-center rounded-xl border border-border bg-card">
        <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (entries.length === 0) {
    return (
      <div className="flex h-48 flex-col items-center justify-center rounded-xl border border-dashed border-border bg-card">
        <BookOpen className="h-8 w-8 text-muted-foreground/50" />
        <p className="mt-3 text-sm text-muted-foreground">
          {locale === 'ro'
            ? 'Nicio intrare în jurnal încă. Începe prin a înregistra primul tău jurnal vocal.'
            : 'No journal entries yet. Start by recording your first voice journal.'}
        </p>
      </div>
    );
  }

  return (
    <div className="space-y-3">
      {entries.map((entry, i) => {
        const ext = entry.extraction;
        const date = entry.createdAt
          ? formatDate(
              typeof entry.createdAt === 'string'
                ? entry.createdAt
                : entry.createdAt,
              locale === 'ro' ? 'ro-RO' : 'en-US'
            )
          : '';

        return (
          <div
            key={entry._id || i}
            className="rounded-xl border border-border bg-card p-4 transition-colors hover:border-primary/30"
          >
            <div className="flex items-start justify-between gap-4">
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2 text-xs text-muted-foreground">
                  <span>{date}</span>
                  {entry.audioDurationSec && (
                    <>
                      <span>•</span>
                      <span>
                        {Math.floor(entry.audioDurationSec / 60)}:
                        {(entry.audioDurationSec % 60).toString().padStart(2, '0')}
                      </span>
                    </>
                  )}
                  <span>•</span>
                  <span className="uppercase">{entry.language}</span>
                </div>

                {ext?.summary && (
                  <p className="mt-2 text-sm font-medium">{ext.summary}</p>
                )}

                <div className="mt-2 flex flex-wrap gap-1.5">
                  {ext?.instrument && (
                    <span className="rounded-md bg-muted px-2 py-0.5 text-xs font-mono">
                      {ext.instrument}
                    </span>
                  )}
                  {ext?.direction && (
                    <span
                      className={`rounded-md px-2 py-0.5 text-xs font-medium ${
                        ext.direction === 'long'
                          ? 'bg-profit/10 text-profit'
                          : 'bg-loss/10 text-loss'
                      }`}
                    >
                      {ext.direction === 'long'
                        ? locale === 'ro'
                          ? 'Long'
                          : 'Long'
                        : locale === 'ro'
                          ? 'Short'
                          : 'Short'}
                    </span>
                  )}
                  {ext?.rMultipleEstimate !== null &&
                    ext?.rMultipleEstimate !== undefined && (
                      <span
                        className={`rounded-md px-2 py-0.5 text-xs font-medium ${
                          ext.rMultipleEstimate > 0
                            ? 'bg-profit/10 text-profit'
                            : 'bg-loss/10 text-loss'
                        }`}
                      >
                        {ext.rMultipleEstimate > 0 ? '+' : ''}
                        {ext.rMultipleEstimate}R
                      </span>
                    )}
                  {ext?.mistakes?.slice(0, 2).map((m) => (
                    <span
                      key={m}
                      className="rounded-md border border-loss/30 bg-loss/5 px-2 py-0.5 text-xs text-loss"
                    >
                      {t(`mistakes.${m}`)}
                    </span>
                  ))}
                </div>

                {entry.transcript && (
                  <p className="mt-2 line-clamp-2 text-xs text-muted-foreground">
                    {entry.transcript}
                  </p>
                )}
              </div>
            </div>
          </div>
        );
      })}
    </div>
  );
}
