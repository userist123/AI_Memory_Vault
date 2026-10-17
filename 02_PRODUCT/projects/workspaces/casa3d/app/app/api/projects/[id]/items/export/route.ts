import { NextResponse } from 'next/server';
import { ownerId } from '@/lib/owner';
import { HttpError } from '@/lib/repo';
import { exportCustomItemsCsv } from '@/lib/custom-items';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
// Doar pentru proprietar: fișierul conține prețurile de achiziție.
export const GET = async (_: Request, { params }: Ctx) => {
  try { const { id } = await params, csv = await exportCustomItemsCsv((await ownerId(true))!, id);
    return new NextResponse(csv, { headers: { 'content-type': 'text/csv; charset=utf-8', 'content-disposition': 'attachment; filename="produse-proprii.csv"', 'cache-control': 'no-store' } });
  } catch (e: any){ if (e instanceof HttpError) return NextResponse.json({ error: e.message }, { status: e.status }); console.error(e); return NextResponse.json({ error: 'Eroare internă.' }, { status: 500 }); }
};
