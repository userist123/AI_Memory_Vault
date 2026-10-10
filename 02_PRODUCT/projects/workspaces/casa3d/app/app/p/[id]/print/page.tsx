import PrintPage from '@/components/PrintPage';
export default async function Page({ params }: { params: Promise<{ id: string }> }){ const { id } = await params; return <PrintPage id={id} />; }
