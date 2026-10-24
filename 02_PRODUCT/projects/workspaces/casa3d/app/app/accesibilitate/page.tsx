import { headers } from 'next/headers';
import { PrefsProvider } from '@/lib/prefs';
import { detectLang } from '@/lib/i18n';
import StatementView from '@/components/StatementView';
export const metadata = { title: 'Accesibilitate — Casa mea 3D', robots: { index: false, follow: false } };
export const dynamic = 'force-dynamic';

// Declarația de accesibilitate (UI/UX P3-17): publică (și prin tunel), în limba cititorului, ca linkul clientului.
export default async function Page(){
  const lang = detectLang((await headers()).get('accept-language'));
  return <PrefsProvider initialLang={lang}><StatementView /></PrefsProvider>;
}
