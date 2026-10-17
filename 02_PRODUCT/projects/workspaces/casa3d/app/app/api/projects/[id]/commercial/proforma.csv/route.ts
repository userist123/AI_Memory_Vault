import { NextResponse } from 'next/server';
import { HttpError } from '@/lib/repo';
import { ownerId } from '@/lib/owner';
import { getProformaCsv } from '@/lib/commercial';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
// CSV generic (cod; denumire; UM; cantitate; preț unitar fără TVA; cota TVA; valoare) pentru importul în programul de facturare.
export const GET = async (_: Request, { params }: Ctx) => {
  const { id } = await params;
  try { const owner = (await ownerId(true))!, { filename, data } = await getProformaCsv(owner, id);
    return new NextResponse(data, { headers: { 'content-type': 'text/csv; charset=utf-8', 'content-disposition': `attachment; filename="${filename}"`, 'cache-control': 'no-store' } }); }
  catch (e: any){ if (e instanceof HttpError) return NextResponse.json({ error: e.message }, { status: e.status }); console.error(e); return NextResponse.json({ error: 'Eroare internă.' }, { status: 500 }); }
};
