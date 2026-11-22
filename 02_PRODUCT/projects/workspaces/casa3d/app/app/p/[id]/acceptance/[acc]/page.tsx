import AcceptancePage from '@/components/OfferAcceptance';
// Confirmarea unei acceptări a ofertei (doar proprietarul): document A4 de tipărit, cu tot conținutul înghețat și amprenta lui.
export default async function Page({ params }: { params: Promise<{ id: string; acc: string }> }){ const { id, acc } = await params; return <AcceptancePage id={id} acc={acc} />; }
