import { notFound } from 'next/navigation';
import { headers } from 'next/headers';
import { PrefsProvider } from '@/lib/prefs';
import { detectLang, t } from '@/lib/i18n';
import { getPublicOffer } from '@/lib/rfq';
import RfqOfferForm from '@/components/RfqOfferForm';
export const dynamic = 'force-dynamic';
// titlul filei în limba browserului (textele stau în dicționar); fără indexare și fără Referer
export async function generateMetadata(){ const lang = detectLang((await headers()).get('accept-language'));
  return { title: `${t(lang, 'rfq.p.pageTitle')} — Casa mea 3D`, robots: { index: false, follow: false }, referrer: 'no-referrer' as const }; }

// Cererea de ofertă pentru un meseriaș: pagină publică, fără cont și fără cookie de proprietar; un link necunoscut, revocat
// sau expirat dă 404. Pagina primește doar cererea și oferta proprie a meseriașului (lib/rfq.ts getPublicOffer): nimic despre
// client, buget sau celelalte oferte. După trimitere arată rezumatul, doar de citit.
export default async function OfferPage({ params }: { params: Promise<{ token: string }> }){
  const { token } = await params;
  let initial; try { initial = await getPublicOffer(token); } catch { notFound(); }
  const lang = detectLang((await headers()).get('accept-language'));
  return <PrefsProvider initialLang={lang}><RfqOfferForm token={token} initial={initial} /></PrefsProvider>;
}
