import { notFound } from 'next/navigation';
import { resolveShare } from '@/lib/share';
import { getCatalog } from '@/lib/repo';
import SharedView from '@/components/SharedView';
export const dynamic = 'force-dynamic';
export const metadata = { robots: { index: false, follow: false } };

export default async function SharePage({ params }: { params: Promise<{ token: string }> }){
  const { token } = await params;
  let view; try { view = await resolveShare(token); } catch { notFound(); }
  const catalog = await getCatalog();
  return <SharedView view={{ ...view, createdAt: new Date(view.createdAt).toISOString() }} catalog={catalog} />;
}
