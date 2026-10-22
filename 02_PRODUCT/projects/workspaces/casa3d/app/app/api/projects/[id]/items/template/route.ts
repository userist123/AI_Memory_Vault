import { NextResponse } from 'next/server';
import { customTemplateCsv } from '@/core/custom-csv';
export const dynamic = 'force-static';
export const GET = async () => new NextResponse(customTemplateCsv(), { headers: { 'content-type': 'text/csv; charset=utf-8', 'content-disposition': 'attachment; filename="sablon-produse-proprii.csv"' } });
