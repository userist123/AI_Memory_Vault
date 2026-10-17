import { notFound } from 'next/navigation';
import { resolveShare } from '@/lib/share';
import PresentView from '@/components/PresentView';
import { getCatalog, getMaterials } from '@/lib/repo';
import { progressForShare } from '@/lib/client-progress';
export const dynamic = 'force-dynamic';
export const metadata = { robots: { index: false, follow: false } };

// Linkul pentru client: prezentarea reviziei partajate (fără cont, fără editare) cu păreri pe fiecare cameră.
export default async function SharePage({ params }: { params: Promise<{ token: string }> }){
  const { token } = await params;
  let view; try { view = await resolveShare(token); } catch { notFound(); }
  // catalogul vine de pe server: clientul nu atinge alte API-uri și nu primește cookie de proprietar
  const [catalog, mc, progress] = await Promise.all([getCatalog(), getMaterials(), progressForShare(token)]);
  return <PresentView share={{ token, name: view.projectName, snapshot: view.snapshot, revisionNumber: view.revisionNumber, catalog, mc, progress }} />;
}
