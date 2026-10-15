import ContractPage from '@/components/ContractPage';
export default async function Page({ params }: { params: Promise<{ id: string }> }){ const { id } = await params; return <ContractPage id={id} kind="offer" />; }
