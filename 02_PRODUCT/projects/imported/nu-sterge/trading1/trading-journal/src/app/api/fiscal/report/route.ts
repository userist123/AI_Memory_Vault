import { NextRequest, NextResponse } from 'next/server';
import { getUserIdFromRequest, getCurrentUser } from '@/lib/auth/session';
import { getTradesByUser } from '@/lib/db/mongo';
import { computeFiscalReport } from '@/lib/fiscal/calculator';
import { isFeatureEnabled } from '@/lib/billing/plans';

export const runtime = 'nodejs';
export const maxDuration = 60;

export async function GET(req: NextRequest) {
  try {
    const user = await getCurrentUser();
    if (!user) {
      return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
    }

    const { searchParams } = new URL(req.url);
    const yearParam = searchParams.get('year');
    const year = yearParam ? parseInt(yearParam) : new Date().getFullYear() - 1;

    if (isNaN(year) || year < 2020 || year > 2030) {
      return NextResponse.json({ error: 'Invalid year' }, { status: 400 });
    }

    // Feature gating: Free plan gets preview (summary only, no detailed breakdown)
    const hasFullAccess = isFeatureEnabled(user.plan, 'fiscalModuleFull');

    // Load ALL trades for the year (fiscal report needs complete picture)
    const yearStart = new Date(`${year}-01-01T00:00:00Z`);
    const trades = await getTradesByUser(user._id!, {
      limit: 10000,
      since: yearStart,
    });

    const report = await computeFiscalReport(user._id!, trades, year);

    if (!hasFullAccess) {
      // Return summary only - hide trade-level detail
      return NextResponse.json({
        report: {
          ...report,
          categories: Object.fromEntries(
            Object.entries(report.categories).map(([key, cat]) => [
              key,
              { ...cat, trades: [] }, // strip trade detail
            ])
          ),
        },
        hasFullAccess: false,
        upgradeRequired: true,
      });
    }

    return NextResponse.json({ report, hasFullAccess: true });
  } catch (err: unknown) {
    const e = err as { message?: string };
    console.error('[Fiscal] Error:', e);
    return NextResponse.json(
      { error: 'Fiscal report failed', details: e.message },
      { status: 500 }
    );
  }
}
