import { NextResponse } from 'next/server';
import { HttpError } from '@/lib/repo';
import { ownerId } from '@/lib/owner';
import { exportProjectIfc } from '@/lib/ifcExport';
import { logError } from '@/lib/log';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
export const GET = async (_req: Request, { params }: Ctx) => {
  const { id } = await params;
  try {
    const owner = (await ownerId(true))!, { filename, body } = await exportProjectIfc(owner, id);
    return new Response(body, { status: 200, headers: { 'content-type': 'application/x-step', 'content-disposition': `attachment; filename="${filename}"`, 'cache-control': 'no-store' } });
  } catch (e: any){
    if (e instanceof HttpError) return NextResponse.json({ error: e.message, details: e.details ?? null }, { status: e.status });
    logError('api/projects/[id]/export.ifc', e); return NextResponse.json({ error: 'Eroare internă.' }, { status: 500 });
  }
};
