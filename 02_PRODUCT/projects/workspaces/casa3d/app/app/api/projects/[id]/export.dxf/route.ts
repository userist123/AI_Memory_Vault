import { NextResponse } from 'next/server';
import { HttpError } from '@/lib/repo';
import { ownerId } from '@/lib/owner';
import { exportProjectDxf } from '@/lib/dxfExport';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
export const GET = async (req: Request, { params }: Ctx) => {
  const { id } = await params, lang = new URL(req.url).searchParams.get('lang') === 'en' ? 'en' : 'ro';
  try {
    const owner = (await ownerId(true))!, { filename, body } = await exportProjectDxf(owner, id, lang);
    return new Response(body, { status: 200, headers: { 'content-type': 'application/dxf', 'content-disposition': `attachment; filename="${filename}"`, 'cache-control': 'no-store' } });
  } catch (e: any){
    if (e instanceof HttpError) return NextResponse.json({ error: e.message, details: e.details ?? null }, { status: e.status });
    console.error(e); return NextResponse.json({ error: 'Eroare internă.' }, { status: 500 });
  }
};
