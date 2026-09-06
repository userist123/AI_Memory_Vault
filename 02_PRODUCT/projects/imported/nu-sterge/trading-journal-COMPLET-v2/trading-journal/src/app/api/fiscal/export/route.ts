import { NextRequest, NextResponse } from 'next/server';
import { getCurrentUser } from '@/lib/auth/session';
import { getTradesByUser } from '@/lib/db/mongo';
import { computeFiscalReport } from '@/lib/fiscal/calculator';
import { exportFiscalCsv, exportD212Summary } from '@/lib/fiscal/exporter';
import { isFeatureEnabled } from '@/lib/billing/plans';

export const runtime = 'nodejs';
export const maxDuration = 60;

export async function GET(req: NextRequest) {
  try {
    const user = await getCurrentUser();
    if (!user) {
      return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
    }

    if (!isFeatureEnabled(user.plan, 'fiscalModuleFull')) {
      return NextResponse.json(
        {
          error: 'upgrade_required',
          message: 'Exportul fiscal este disponibil pentru planurile Pro și Elite.',
          upgradeUrl: '/pricing',
        },
        { status: 402 }
      );
    }

    const { searchParams } = new URL(req.url);
    const yearParam = searchParams.get('year');
    const format = searchParams.get('format') || 'csv';
    const year = yearParam ? parseInt(yearParam) : new Date().getFullYear() - 1;

    if (isNaN(year) || year < 2020 || year > 2030) {
      return NextResponse.json({ error: 'Invalid year' }, { status: 400 });
    }

    const yearStart = new Date(`${year}-01-01T00:00:00Z`);
    const trades = await getTradesByUser(user._id!, {
      limit: 10000,
      since: yearStart,
    });

    const report = await computeFiscalReport(user._id!, trades, year);

    let body: string;
    let filename: string;

    if (format === 'd212') {
      body = exportD212Summary(report);
      filename = `D212_rezumat_${year}.csv`;
    } else {
      body = exportFiscalCsv(report);
      filename = `raport_fiscal_${year}.csv`;
    }

    return new NextResponse(body, {
      status: 200,
      headers: {
        'Content-Type': 'text/csv; charset=utf-8',
        'Content-Disposition': `attachment; filename="${filename}"`,
      },
    });
  } catch (err: unknown) {
    const e = err as { message?: string };
    console.error('[Fiscal Export] Error:', e);
    return NextResponse.json(
      { error: 'Export failed', details: e.message },
      { status: 500 }
    );
  }
}
