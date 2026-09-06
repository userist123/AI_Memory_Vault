import { defineRouting } from 'next-intl/routing';
import { createNavigation } from 'next-intl/navigation';

export const routing = defineRouting({
  locales: ['ro', 'en'] as const,
  defaultLocale: 'ro',
  localePrefix: 'always',
  pathnames: {
    '/': '/',
    '/dashboard': {
      ro: '/dashboard',
      en: '/dashboard',
    },
    '/trades': {
      ro: '/tranzactii',
      en: '/trades',
    },
    '/journal': {
      ro: '/jurnal',
      en: '/journal',
    },
    '/analytics': {
      ro: '/analiza',
      en: '/analytics',
    },
    '/calendar': {
      ro: '/calendar',
      en: '/calendar',
    },
    '/calculators': {
      ro: '/calculatoare',
      en: '/calculators',
    },
    '/settings': {
      ro: '/setari',
      en: '/settings',
    },
    '/pricing': {
      ro: '/preturi',
      en: '/pricing',
    },
    '/login': {
      ro: '/autentificare',
      en: '/login',
    },
    '/signup': {
      ro: '/inregistrare',
      en: '/signup',
    },
  },
});

export type Locale = (typeof routing.locales)[number];

export const { Link, redirect, usePathname, useRouter, getPathname } =
  createNavigation(routing);
