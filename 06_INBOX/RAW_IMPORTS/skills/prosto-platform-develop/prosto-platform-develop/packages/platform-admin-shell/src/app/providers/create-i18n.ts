import { createI18n as CreateI18n } from 'vue-i18n';
import { en } from '@/shared/locales';

export function createI18n() {
  return CreateI18n({
    locale: 'en',
    messages: { en },
  });
}
