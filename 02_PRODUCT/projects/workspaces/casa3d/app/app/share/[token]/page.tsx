import { notFound } from 'next/navigation';
import { resolveShare } from '@/lib/share';
import PresentView from '@/components/PresentView';
export const dynamic = 'force-dynamic';
export const metadata = { robots: { index: false, follow: false } };

// Linkul pentru client: prezentarea reviziei partajate (fără cont, fără editare) cu păreri pe fiecare cameră.
export default async function SharePage({ params }: { params: Promise<{ token: string }> }){
  const { token } = await params;
  let view; try { view = await resolveShare(token); } catch { notFound(); }
  return <PresentView share={{ token, name: view.projectName, snapshot: view.snapshot, revisionNumber: view.revisionNumber }} />;
}
