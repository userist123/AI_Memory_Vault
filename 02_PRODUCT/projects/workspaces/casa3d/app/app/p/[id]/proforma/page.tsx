import ProformaView from '@/components/ProformaView';
export default async function Page({ params }: { params: Promise<{ id: string }> }){ const { id } = await params; return <ProformaView id={id} />; }
