import { NextResponse } from 'next/server';
import { HttpError } from '@/lib/repo';
import { ownerId } from '@/lib/owner';
import { getProformaCsv } from '@/lib/commercial';
import { logError } from '@/lib/log';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
// CSV generic (cod; denumire; UM; cantitate; preț unitar fără TVA; cota TVA; valoare) pentru importul în programul de facturare.
export const GET = async (req: Request, { params }: Ctx) => {
  const { id } = await params, u = new URL(req.url), s = u.searchParams.get('s'), n = u.searchParams.get('n');
  try { if ((s != null || n != null) && !(s && /^[\w.-]{1,20}$/.test(s) && n && /^\d{1,9}$/.test(n))) throw new HttpError(400, 'Seria sau numărul proformei sunt invalide.');
    const owner = (await ownerId(true))!, { filename, data } = await getProformaCsv(owner, id, s && n ? { series: s, number: Number(n) } : undefined);
    return new NextResponse(data, { headers: { 'content-type': 'text/csv; charset=utf-8', 'content-disposition': `attachment; filename="${filename}"`, 'cache-control': 'no-store' } }); }
  catch (e: any){ if (e instanceof HttpError) return NextResponse.json({ error: e.message }, { status: e.status }); logError('api/projects/[id]/commercial/proforma.csv', e); return NextResponse.json({ error: 'Eroare internă.' }, { status: 500 }); }
};
