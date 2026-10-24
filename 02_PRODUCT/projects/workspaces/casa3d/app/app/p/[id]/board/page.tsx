import MoodBoard from '@/components/MoodBoard';
export default async function Page({ params }: { params: Promise<{ id: string }> }){ const { id } = await params; return <MoodBoard id={id} />; }
