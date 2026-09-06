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
 * XTB xStation CSV Export
 * Export from xStation 5: Statement → Export → CSV
 *
 * Typical columns:
 * ID | Type | Symbol | Volume | Open time | Close time |
 * Open price | Close price | SL | TP | Commission | Swap | Profit | Comment
 */
export const xtbImporter: BrokerImporter = {
  broker: 'xtb',
  displayName: 'XTB xStation',
  fileTypes: ['csv'],
  description: {
    ro: 'Exportă din xStation 5: Statement → Export → CSV',
    en: 'Export from xStation 5: Statement → Export → CSV',
  },

  detectSignature: (content: string | Buffer) => {
    const text = typeof content === 'string' ? content : content.toString('utf-8');
    const firstLines = text.split('\n').slice(0, 3).join(' ').toLowerCase();
    return (
      (firstLines.includes('open time') || firstLines.includes('open_time')) &&
      firstLines.includes('symbol') &&
      (firstLines.includes('xtb') ||
        firstLines.includes('xstation') ||
        (firstLines.includes('volume') &&
          firstLines.includes('profit') &&
          firstLines.includes('swap')))
    );
  },

  parse: async (file: File, userId: string): Promise<ParsedImport> => {
    const text = await file.text();
    const errors: ImportError[] = [];
    const trades: Omit<Trade, '_id' | 'createdAt' | 'updatedAt'>[] = [];

    // XTB sometimes uses ; as separator (European CSV)
    const sep = text.split('\n')[0].includes(';') ? ';' : ',';

    const parsed = Papa.parse<RawTradeRow>(text, {
      header: true,
      skipEmptyLines: true,
      delimiter: sep,
      transformHeader: (h) => h.trim(),
    });

    const rows = parsed.data;

    // Helper to find column by name (case-insensitive, partial match)
    const findKey = (row: RawTradeRow, keywords: string[]): string | undefined => {
      const keys = Object.keys(row);
      for (const kw of keywords) {
        const match = keys.find((k) => k.toLowerCase().includes(kw.toLowerCase()));
        if (match) return match;
      }
      return undefined;
    };

    rows.forEach((row, idx) => {
      try {
        const symbolKey = findKey(row, ['symbol', 'instrument']);
        const typeKey = findKey(row, ['type', 'cmd', 'side']);
        const volumeKey = findKey(row, ['volume', 'size', 'lots']);
        const openTimeKey = findKey(row, ['open time', 'open_time', 'entry time']);
        const closeTimeKey = findKey(row, ['close time', 'close_time', 'exit time']);
        const openPriceKey = findKey(row, ['open price', 'open_price', 'entry']);
        const closePriceKey = findKey(row, ['close price', 'close_price', 'exit']);
        const slKey = findKey(row, ['sl', 'stop loss', 'stop_loss']);
        const tpKey = findKey(row, ['tp', 'take profit', 'take_profit']);
        const commKey = findKey(row, ['commission']);
        const swapKey = findKey(row, ['swap', 'rollover']);
        const profitKey = findKey(row, ['profit', 'p/l', 'pnl', 'net']);
        const idKey = findKey(row, ['id', 'ticket', 'order']);

        if (!symbolKey) return;
        const symbol = String(row[symbolKey] || '').trim();
        if (!symbol) return;

        const type = String(row[typeKey || ''] || '').toLowerCase();
        // Skip non-trade rows (balance, deposit, etc.)
        if (!['buy', 'sell', 'long', 'short'].some((t) => type.includes(t))) {
          return;
        }

        const volume = volumeKey ? parseNumber(row[volumeKey]) : null;
        const openTime = openTimeKey ? parseDate(String(row[openTimeKey])) : null;
        const closeTime = closeTimeKey ? parseDate(String(row[closeTimeKey])) : null;
        const openPrice = openPriceKey ? parseNumber(row[openPriceKey]) : null;
        const closePrice = closePriceKey ? parseNumber(row[closePriceKey]) : null;
        const sl = slKey ? parseNumber(row[slKey]) : null;
        const tp = tpKey ? parseNumber(row[tpKey]) : null;
        const comm = commKey ? parseNumber(row[commKey]) ?? 0 : 0;
        const swap = swapKey ? parseNumber(row[swapKey]) ?? 0 : 0;
        const profit = profitKey ? parseNumber(row[profitKey]) : null;
        const ticketId = idKey ? String(row[idKey]).trim() : null;

        if (!volume || !openTime || !openPrice) {
          errors.push({
            row: idx + 2,
            message: `Incomplete data for ${symbol}`,
          });
          return;
        }

        trades.push({
          userId,
          externalId: ticketId ? `xtb_${ticketId}` : null,
          broker: 'xtb',
          accountId: null,
          symbol,
          assetClass: detectAssetClass(symbol),
          direction: type.includes('buy') || type.includes('long') ? 'long' : 'short',
          status: closeTime && closePrice ? 'closed' : 'open',
          entryPrice: openPrice,
          exitPrice: closePrice,
          stopLoss: sl,
          takeProfit: tp,
          quantity: volume,
          lotSize: volume,
          entryTime: openTime,
          exitTime: closeTime,
          pnl: profit,
          pnlPercent: null,
          commission: Math.abs(comm),
          swap,
          currency: 'EUR', // XTB Romania defaults to EUR
          rMultiple: null,
          strategy: null,
          tags: [],
          notes: null,
          screenshots: [],
          importSource: file.name,
          importBatch: null,
        });
      } catch (err) {
        errors.push({ row: idx + 2, message: String(err) });
      }
    });

    return {
      trades,
      errors,
      totalRows: rows.length,
    };
  },
};
