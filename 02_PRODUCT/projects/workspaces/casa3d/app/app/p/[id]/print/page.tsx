import PrintView from '@/components/PrintView';
export default async function Page({ params }: { params: Promise<{ id: string }> }){ const { id } = await params; return <PrintView id={id} />; }
