import Papa from 'papaparse';
import type {
  BrokerImporter,
  ParsedImport,
  RawTradeRow,
  Trade,
  ImportError,
} from '@/types/trade';
import { detectAssetClass, parseNumber, parseDate } from './utils';

/**
 * Trading 212 CSV Export
 * Export from Trading 212: Settings → History → Export → CSV
 *
 * Columns:
 * Action | Time | ISIN | Ticker | Name | No. of shares | Price / share |
 * Currency (Price / share) | Exchange rate | Result | Currency (Result) |
 * Total | Currency (Total) | Withholding tax | ...
 */
export const trading212Importer: BrokerImporter = {
  broker: 'trading212',
  displayName: 'Trading 212',
  fileTypes: ['csv'],
  description: {
    ro: 'Exportă din Trading 212: Settings → History → Export → CSV',
    en: 'Export from Trading 212: Settings → History → Export → CSV',
  },

  detectSignature: (content: string | Buffer) => {
    const text = typeof content === 'string' ? content : content.toString('utf-8');
    const firstLine = text.split('\n')[0].toLowerCase();
    return (
      firstLine.includes('action') &&
      firstLine.includes('ticker') &&
      (firstLine.includes('no. of shares') || firstLine.includes('shares'))
    );
  },

  parse: async (file: File, userId: string): Promise<ParsedImport> => {
    const text = await file.text();
    const errors: ImportError[] = [];
    const trades: Omit<Trade, '_id' | 'createdAt' | 'updatedAt'>[] = [];

    const parsed = Papa.parse<RawTradeRow>(text, {
      header: true,
      skipEmptyLines: true,
      transformHeader: (h) => h.trim(),
    });

    const rows = parsed.data;

    // Group rows by ticker for FIFO matching
    const byTicker = new Map<string, Array<{ row: RawTradeRow; idx: number }>>();

    rows.forEach((row, idx) => {
      const action = String(row['Action'] || '').toLowerCase();
      const ticker = String(row['Ticker'] || '').trim();

      // We only care about market buy/sell
      if (
        !ticker ||
        !['market buy', 'market sell', 'limit buy', 'limit sell', 'stop buy', 'stop sell'].some((a) =>
          action.includes(a)
        )
      ) {
        return;
      }

      if (!byTicker.has(ticker)) byTicker.set(ticker, []);
      byTicker.get(ticker)!.push({ row, idx });
    });

    byTicker.forEach((entries, ticker) => {
      // Sort by time asc
      entries.sort((a, b) => {
        const da = parseDate(String(a.row['Time'] || ''));
        const db = parseDate(String(b.row['Time'] || ''));
        if (!da || !db) return 0;
        return da.getTime() - db.getTime();
      });

      const openPositions: Array<{
        quantity: number;
        price: number;
        date: Date;
        currency: string;
        rowIdx: number;
      }> = [];

      entries.forEach(({ row, idx }) => {
        try {
          const action = String(row['Action'] || '').toLowerCase();
          const isBuy = action.includes('buy');
          const quantity = parseNumber(row['No. of shares'] || row['Shares'] || row['Quantity']);
          const price = parseNumber(row['Price / share'] || row['Price']);
          const date = parseDate(String(row['Time'] || ''));
          const currency = String(
            row['Currency (Price / share)'] || row['Currency'] || 'USD'
          ).trim() as 'USD' | 'EUR' | 'GBP';

          if (!quantity || !price || !date) {
            errors.push({
              row: idx + 2,
              message: `Missing fields for ${ticker}`,
            });
            return;
          }

          if (isBuy) {
            openPositions.push({ quantity, price, date, currency, rowIdx: idx });
          } else {
            // SELL - match FIFO
            let remaining = quantity;
            while (remaining > 0 && openPositions.length > 0) {
              const open = openPositions[0];
              const matchQty = Math.min(remaining, open.quantity);

              const pnlLocal = (price - open.price) * matchQty;

              trades.push({
                userId,
                externalId: `t212_${ticker}_${open.date.toISOString()}_${date.toISOString()}`,
                broker: 'trading212',
                accountId: null,
                symbol: ticker,
                assetClass: detectAssetClass(ticker),
                direction: 'long',
                status: 'closed',
                entryPrice: open.price,
                exitPrice: price,
                stopLoss: null,
                takeProfit: null,
                quantity: matchQty,
                lotSize: null,
                entryTime: open.date,
                exitTime: date,
                pnl: pnlLocal,
                pnlPercent: ((price - open.price) / open.price) * 100,
                commission: 0, // T212 shows 0 commission for most
                swap: 0,
                currency: ['USD', 'EUR', 'GBP'].includes(open.currency)
                  ? (open.currency as 'USD' | 'EUR' | 'GBP')
                  : 'USD',
                rMultiple: null,
                strategy: null,
                tags: [],
                notes: null,
                screenshots: [],
                importSource: file.name,
                importBatch: null,
              });

              open.quantity -= matchQty;
              remaining -= matchQty;
              if (open.quantity <= 0) openPositions.shift();
            }
          }
        } catch (err) {
          errors.push({ row: idx + 2, message: String(err) });
        }
      });

      // Remaining opens
      openPositions.forEach((open) => {
        trades.push({
          userId,
          externalId: `t212_${ticker}_${open.date.toISOString()}_open`,
          broker: 'trading212',
          accountId: null,
          symbol: ticker,
          assetClass: detectAssetClass(ticker),
          direction: 'long',
          status: 'open',
          entryPrice: open.price,
          exitPrice: null,
          stopLoss: null,
          takeProfit: null,
          quantity: open.quantity,
          lotSize: null,
          entryTime: open.date,
          exitTime: null,
          pnl: null,
          pnlPercent: null,
          commission: 0,
          swap: 0,
          currency: ['USD', 'EUR', 'GBP'].includes(open.currency)
            ? (open.currency as 'USD' | 'EUR' | 'GBP')
            : 'USD',
          rMultiple: null,
          strategy: null,
          tags: [],
          notes: null,
          screenshots: [],
          importSource: file.name,
          importBatch: null,
        });
      });
    });

    return {
      trades,
      errors,
      totalRows: rows.length,
    };
  },
};
