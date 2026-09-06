import { NextRequest, NextResponse } from 'next/server';
import { detectImporter, getImporterByBroker } from '@/lib/importers';
import { saveTradesBatch } from '@/lib/db/mongo';
import { getUserIdFromRequest } from '@/lib/auth/session';
import { checkQuota, quotaExceededResponse } from '@/lib/billing/quota';
import { incrementUsage } from '@/lib/db/usage';
import type { Broker, ImportResult } from '@/types/trade';

export const runtime = 'nodejs';
export const maxDuration = 60;

export async function POST(req: NextRequest) {
  try {
    const userId = await getUserIdFromRequest(req);
    if (!userId) {
      return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
    }

    const formData = await req.formData();
    const file = formData.get('file') as File | null;
    const brokerHint = formData.get('broker') as string | null;

    if (!file) {
      return NextResponse.json({ error: 'No file provided' }, { status: 400 });
    }

    if (file.size > 10 * 1024 * 1024) {
      return NextResponse.json(
        { error: 'File too large (max 10 MB)' },
        { status: 413 }
      );
    }

    const importer = brokerHint
      ? getImporterByBroker(brokerHint as Broker) || (await detectImporter(file))
      : await detectImporter(file);

    const parsed = await importer.parse(file, userId);

    if (parsed.trades.length === 0) {
      return NextResponse.json(
        {
          success: false,
          broker: importer.broker,
          fileName: file.name,
          totalRows: parsed.totalRows,
          parsedRows: 0,
          importedTrades: 0,
          duplicates: 0,
          errors: parsed.errors,
          trades: [],
          batchId: '',
        } as ImportResult,
        { status: 200 }
      );
    }

    // Enforce quota BEFORE saving (based on free plan = 50 trades/month)
    const quotaCheck = await checkQuota(userId, 'tradeImport');
    const toImport = parsed.trades.length;

    if (quotaCheck.limit !== -1 && quotaCheck.used + toImport > quotaCheck.limit) {
      const remaining = Math.max(0, quotaCheck.limit - quotaCheck.used);
      if (remaining === 0) {
        const resp = quotaExceededResponse(quotaCheck);
        return NextResponse.json(resp.body, { status: resp.status });
      }
      // Partial import: truncate to fit quota
      parsed.trades = parsed.trades.slice(0, remaining);
      parsed.errors.push({
        row: 0,
        message: `Plan ${quotaCheck.plan.toUpperCase()}: ${remaining} trades importate (din ${toImport}). Upgrade la Pro pentru nelimitat.`,
      });
    }

    const batchId = `batch_${Date.now()}_${Math.random().toString(36).slice(2, 9)}`;
    const tradesWithBatch = parsed.trades.map((t) => ({ ...t, importBatch: batchId }));
    const { inserted, duplicates, savedTrades } = await saveTradesBatch(tradesWithBatch);

    // Increment usage by actually-inserted count (not duplicates)
    if (inserted > 0) {
      await incrementUsage(userId, 'tradeImport', 'month', inserted);
    }

    const result: ImportResult = {
      success: true,
      broker: importer.broker,
      fileName: file.name,
      totalRows: parsed.totalRows,
      parsedRows: parsed.trades.length,
      importedTrades: inserted,
      duplicates,
      errors: parsed.errors,
      trades: savedTrades,
      batchId,
    };

    return NextResponse.json(result);
  } catch (err: unknown) {
    const e = err as { message?: string };
    console.error('[Import] Error:', e);
    return NextResponse.json(
      { error: 'Import failed', details: e.message || 'Unknown error' },
      { status: 500 }
    );
  }
}

export async function GET() {
  const { importers } = await import('@/lib/importers');
  return NextResponse.json({
    importers: importers.map((i) => ({
      broker: i.broker,
      displayName: i.displayName,
      fileTypes: i.fileTypes,
      description: i.description,
    })),
  });
}
