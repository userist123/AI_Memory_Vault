import HandoverReport from '@/components/HandoverReport';
export default async function Page({ params }: { params: Promise<{ id: string }> }){ const { id } = await params; return <HandoverReport id={id} />; }
