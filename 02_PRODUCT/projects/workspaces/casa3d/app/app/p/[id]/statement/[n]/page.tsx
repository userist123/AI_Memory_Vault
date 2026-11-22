import WorkStatementView from '@/components/WorkStatementView';
export default async function Page({ params }: { params: Promise<{ id: string; n: string }> }){ const { id, n } = await params; return <WorkStatementView id={id} n={Number(n)} />; }
