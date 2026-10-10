import { notFound } from 'next/navigation';
import { resolveShare } from '@/lib/share';
import { getCatalog } from '@/lib/repo';
import SharedPlanView from '@/components/SharedPlanView';
export const dynamic = 'force-dynamic';
export const metadata = { robots: { index: false, follow: false } };

export default async function SharePage({ params }: { params: Promise<{ token: string }> }){
  const { token } = await params;
  let view; try { view = await resolveShare(token); } catch { notFound(); }
  const catalog = await getCatalog();
  return (<main className="home" style={{ maxWidth: 1100 }}>
    <h1 style={{ marginBottom: 4 }}>{view.projectName}</h1>
    <p className="muted">Revizia {view.revisionNumber}{view.note ? ` · ${view.note}` : ''} · {new Date(view.createdAt).toLocaleString('ro-RO')} · doar vizualizare</p>
    <SharedPlanView snap={view.snapshot} catalog={catalog} />
    <p className="prov">Acest link arată o singură revizie, înghețată. Modificările ulterioare ale proiectului nu apar aici. Prețurile și dimensiunile vin din catalogul aplicației, cu data verificării lor.</p>
  </main>);
}
