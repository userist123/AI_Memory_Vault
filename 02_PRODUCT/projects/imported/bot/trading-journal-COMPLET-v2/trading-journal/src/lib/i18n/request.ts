import { getRequestConfig } from 'next-intl/server';
import { routing } from './routing';

export default getRequestConfig(async ({ requestLocale }) => {
  let locale = await requestLocale;

  if (!locale || !routing.locales.includes(locale as 'ro' | 'en')) {
    locale = routing.defaultLocale;
  }

  return {
    locale,
    messages: (await import(`../../../public/locales/${locale}/common.json`))
      .default,
    timeZone: 'Europe/Bucharest',
    now: new Date(),
    formats: {
      dateTime: {
        short: {
          day: 'numeric',
          month: 'short',
          year: 'numeric',
        },
        long: {
          day: 'numeric',
          month: 'long',
          year: 'numeric',
          hour: '2-digit',
          minute: '2-digit',
        },
      },
      number: {
        currency: {
          style: 'currency',
          currency: 'EUR',
          maximumFractionDigits: 2,
        },
        ron: {
          style: 'currency',
          currency: 'RON',
          maximumFractionDigits: 2,
        },
        usd: {
          style: 'currency',
          currency: 'USD',
          maximumFractionDigits: 2,
        },
        percent: {
          style: 'percent',
          maximumFractionDigits: 2,
        },
      },
    },
  };
});
