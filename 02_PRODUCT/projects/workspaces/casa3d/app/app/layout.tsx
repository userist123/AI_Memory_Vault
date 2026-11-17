// Fonturile sunt servite de aplicație (@fontsource, OFL-1.1), nu de Google: niciun IP de vizitator nu pleacă la terți
// și politica CSP rămâne 'self'. Aceleași nume de familie ca înainte („Instrument Sans”, „Newsreader”).
import '@fontsource/instrument-sans/400.css'; import '@fontsource/instrument-sans/500.css'; import '@fontsource/instrument-sans/600.css';
import '@fontsource/newsreader/400.css'; import '@fontsource/newsreader/500.css';
import './tokens.css';
import './globals.css';
import { headers } from 'next/headers';
import { PrefsProvider } from '@/lib/prefs';
import { DEFAULT_LANG, detectLang, type Lang } from '@/lib/i18n';
import DialogHost from '@/components/DialogHost';
export const metadata = { title: 'Casa mea 3D', description: 'Floor plan, real furniture and budget in one project. / Plan, mobilier real și buget, într-un singur proiect.' };
// Citirea antetelor face fiecare pagină dinamică: Next pune nonce-ul din CSP (proxy.ts) pe scripturile proprii;
// o pagină pre-randată static n-ar avea nonce și ar fi blocată de 'strict-dynamic'.
// Limba primei randări (și a paginii fără JavaScript): cea a browserului (Accept-Language), altfel limba implicită a aplicației
// (română). După montare, alegerea salvată de utilizator (localStorage) are prioritate.
export default async function RootLayout({ children }: { children: React.ReactNode }){
  const al = (await headers()).get('accept-language'), lang: Lang = al ? detectLang(al) : DEFAULT_LANG;
  return (<html lang={lang}><head></head><body><PrefsProvider initialLang={lang}>{children}<DialogHost /></PrefsProvider></body></html>);
}
