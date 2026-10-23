import { notFound } from 'next/navigation';
import { headers } from 'next/headers';
import { PrefsProvider } from '@/lib/prefs';
import { detectLang } from '@/lib/i18n';
import { resolveShare } from '@/lib/share';
import PresentView from '@/components/PresentView';
import { getCatalog, getMaterials } from '@/lib/repo';
import { progressForShare } from '@/lib/client-progress';
import { shareRenderSet } from '@/lib/share-renders';
export const dynamic = 'force-dynamic';
export const metadata = { robots: { index: false, follow: false } };

// Linkul pentru client: prezentarea reviziei partajate (fără cont, fără editare) cu păreri pe fiecare cameră.
export default async function SharePage({ params }: { params: Promise<{ token: string }> }){
  const { token } = await params;
  let view; try { view = await resolveShare(token); } catch { notFound(); }
  // catalogul vine de pe server: clientul nu atinge alte API-uri și nu primește cookie de proprietar
  // imaginile 3D gata randate de designer (lib/share-renders.ts) intră direct în HTML: coperta apare fără motorul 3D
  const [catalog, mc, progress, renders] = await Promise.all([getCatalog(), getMaterials(), progressForShare(token), shareRenderSet(token).catch(() => ({}))]);
  // limba primei randări vine din browserul clientului (Accept-Language): planșele sunt în HTML, deci nu trebuie să clipească
  // din engleză în română; o preferință salvată (localStorage) se aplică după montare, ca până acum
  const lang = detectLang((await headers()).get('accept-language'));
  return <PrefsProvider initialLang={lang}><PresentView share={{ token, name: view.projectName, snapshot: view.snapshot, revisionNumber: view.revisionNumber, catalog, mc, progress, renders }} /></PrefsProvider>;
}
