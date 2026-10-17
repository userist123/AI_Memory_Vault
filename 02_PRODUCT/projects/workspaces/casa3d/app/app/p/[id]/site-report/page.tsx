import SiteReport from '@/components/SiteReport';
export default async function Page({ params }: { params: Promise<{ id: string }> }){ const { id } = await params; return <SiteReport id={id} />; }
