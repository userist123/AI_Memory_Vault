import { notFound } from 'next/navigation';
import { headers } from 'next/headers';
import { PrefsProvider } from '@/lib/prefs';
import { detectLang } from '@/lib/i18n';
import { getPublicBrief } from '@/lib/client-brief';
import ClientBriefForm from '@/components/ClientBriefForm';
export const dynamic = 'force-dynamic';
export const metadata = { title: 'Chestionar — Casa mea 3D', robots: { index: false, follow: false }, referrer: 'no-referrer' as const };

// Chestionarul clientului: pagină publică, fără cont și fără cookie de proprietar; un link necunoscut, revocat sau expirat dă 404.
// Răspunsurile de până acum vin de pe server, în HTML; după trimitere pagina arată doar mulțumirea.
export default async function BriefPage({ params }: { params: Promise<{ token: string }> }){
  const { token } = await params;
  let initial; try { initial = await getPublicBrief(token); } catch { notFound(); }
  const lang = detectLang((await headers()).get('accept-language'));
  return <PrefsProvider initialLang={lang}><ClientBriefForm token={token} initial={initial} /></PrefsProvider>;
}
