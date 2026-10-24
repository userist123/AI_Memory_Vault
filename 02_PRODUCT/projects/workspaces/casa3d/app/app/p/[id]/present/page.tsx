import PresentView from '@/components/PresentView';
export default async function Page({ params }: { params: Promise<{ id: string }> }){ const { id } = await params; return <PresentView id={id} />; }
