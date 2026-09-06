'use client';

import { useLocale } from 'next-intl';
import { useRouter, usePathname } from '@/lib/i18n/routing';
import { Button } from '@/components/ui/button';
import { Globe } from 'lucide-react';

export function LanguageSwitcher() {
  const locale = useLocale();
  const router = useRouter();
  const pathname = usePathname();

  const switchLocale = (newLocale: 'ro' | 'en') => {
    router.replace(pathname, { locale: newLocale });
  };

  return (
    <div className="flex items-center gap-1 rounded-md border border-border bg-background p-1">
      <Globe className="ml-2 h-4 w-4 text-muted-foreground" />
      <button
        onClick={() => switchLocale('ro')}
        className={`rounded px-2 py-1 text-xs font-medium transition-colors ${
          locale === 'ro'
            ? 'bg-primary text-primary-foreground'
            : 'text-muted-foreground hover:text-foreground'
        }`}
        aria-label="Română"
      >
        RO
      </button>
      <button
        onClick={() => switchLocale('en')}
        className={`rounded px-2 py-1 text-xs font-medium transition-colors ${
          locale === 'en'
            ? 'bg-primary text-primary-foreground'
            : 'text-muted-foreground hover:text-foreground'
        }`}
        aria-label="English"
      >
        EN
      </button>
    </div>
  );
}
