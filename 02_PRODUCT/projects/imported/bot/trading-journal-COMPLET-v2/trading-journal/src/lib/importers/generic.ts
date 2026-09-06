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
 * Generic/Universal CSV importer - fallback
 * Tries to intelligently detect columns from common naming patterns.
 * Works for most broker CSVs including IBKR Flex CSV, cTrader, custom exports.
 */
export const genericCsvImporter: BrokerImporter = {
  broker: 'other',
  displayName: 'Generic CSV',
  fileTypes: ['csv', 'tsv', 'txt'],
  description: {
    ro: 'Orice CSV cu coloane standard (simbol, preț, cantitate, dată, etc.)',
    en: 'Any CSV with standard columns (symbol, price, quantity, date, etc.)',
  },

  detectSignature: () => true, // Always match as last resort

  parse: async (file: File, userId: string): Promise<ParsedImport> => {
    const text = await file.text();
    const errors: ImportError[] = [];
    const trades: Omit<Trade, '_id' | 'createdAt' | 'updatedAt'>[] = [];

    // Auto-detect separator
    const firstLine = text.split('\n')[0];
    const commas = (firstLine.match(/,/g) || []).length;
    const semis = (firstLine.match(/;/g) || []).length;
    const tabs = (firstLine.match(/\t/g) || []).length;
    const sep = tabs > commas && tabs > semis ? '\t' : semis > commas ? ';' : ',';

    const parsed = Papa.parse<RawTradeRow>(text, {
      header: true,
      skipEmptyLines: true,
      delimiter: sep,
      transformHeader: (h) => h.trim(),
    });

    const rows = parsed.data;
    if (rows.length === 0) {
      errors.push({ row: 0, message: 'No data rows found' });
      return { trades: [], errors, totalRows: 0 };
    }

    // Extensive column alias mapping
    const ALIASES = {
      symbol: ['symbol', 'ticker', 'instrument', 'pair', 'asset', 'contract', 'market'],
      direction: ['direction', 'side', 'type', 'action', 'cmd', 'buy/sell', 'order type', 'position'],
      entryPrice: ['entry', 'entry price', 'open price', 'open_price', 'buy price', 'avg entry', 'price in', 'fill price'],
      exitPrice: ['exit', 'exit price', 'close price', 'close_price', 'sell price', 'avg exit', 'price out'],
      quantity: ['quantity', 'qty', 'volume', 'size', 'shares', 'units', 'amount', 'contracts', 'lots'],
      entryTime: ['entry time', 'open time', 'date open', 'open_time', 'date', 'time', 'timestamp', 'entry date'],
      exitTime: ['exit time', 'close time', 'date close', 'close_time', 'exit date'],
      pnl: ['pnl', 'p&l', 'p/l', 'profit', 'gain', 'net pnl', 'realized pnl', 'result', 'profit/loss'],
      commission: ['commission', 'fee', 'fees', 'cost'],
      swap: ['swap', 'rollover', 'overnight'],
      stopLoss: ['stop loss', 'sl', 'stop_loss', 'stoploss'],
      takeProfit: ['take profit', 'tp', 'take_profit', 'takeprofit', 'target'],
      currency: ['currency', 'ccy', 'account currency'],
      externalId: ['id', 'trade id', 'order id', 'ticket', 'position id', 'ref'],
    };

    const firstRow = rows[0];
    const keys = Object.keys(firstRow);

    const findKey = (aliases: string[]): string | null => {
      for (const alias of aliases) {
        const match = keys.find((k) => k.toLowerCase().trim() === alias);
        if (match) return match;
      }
      // Partial match
      for (const alias of aliases) {
        const match = keys.find((k) => k.toLowerCase().trim().includes(alias));
        if (match) return match;
      }
      return null;
    };

    const cols = {
      symbol: findKey(ALIASES.symbol),
      direction: findKey(ALIASES.direction),
      entryPrice: findKey(ALIASES.entryPrice),
      exitPrice: findKey(ALIASES.exitPrice),
      quantity: findKey(ALIASES.quantity),
      entryTime: findKey(ALIASES.entryTime),
      exitTime: findKey(ALIASES.exitTime),
      pnl: findKey(ALIASES.pnl),
      commission: findKey(ALIASES.commission),
      swap: findKey(ALIASES.swap),
      stopLoss: findKey(ALIASES.stopLoss),
      takeProfit: findKey(ALIASES.takeProfit),
      currency: findKey(ALIASES.currency),
      externalId: findKey(ALIASES.externalId),
    };

    // Minimum required: symbol + entryPrice + quantity + (entryTime OR exitTime)
    if (!cols.symbol || !cols.entryPrice || !cols.quantity) {
      errors.push({
        row: 0,
        message: `Could not auto-detect columns. Found: ${keys.join(', ')}. Need at least: symbol, price, quantity.`,
      });
      return { trades: [], errors, totalRows: rows.length };
    }

    rows.forEach((row, idx) => {
      try {
        const symbol = String(row[cols.symbol!] || '').trim();
        if (!symbol) return;

        const directionRaw = cols.direction
          ? String(row[cols.direction] || '').toLowerCase()
          : 'long';
        const direction =
          directionRaw.includes('sell') ||
          directionRaw.includes('short') ||
          directionRaw === 's'
            ? 'short'
            : 'long';

        const entryPrice = parseNumber(row[cols.entryPrice!]);
        const exitPrice = cols.exitPrice ? parseNumber(row[cols.exitPrice]) : null;
        const quantity = parseNumber(row[cols.quantity!]);
        const entryTime = cols.entryTime ? parseDate(String(row[cols.entryTime])) : null;
        const exitTime = cols.exitTime ? parseDate(String(row[cols.exitTime])) : null;
        const pnl = cols.pnl ? parseNumber(row[cols.pnl]) : null;
        const commission = cols.commission ? parseNumber(row[cols.commission]) ?? 0 : 0;
        const swap = cols.swap ? parseNumber(row[cols.swap]) ?? 0 : 0;
        const sl = cols.stopLoss ? parseNumber(row[cols.stopLoss]) : null;
        const tp = cols.takeProfit ? parseNumber(row[cols.takeProfit]) : null;
        const currency = cols.currency
          ? String(row[cols.currency] || 'USD').trim().toUpperCase()
          : 'USD';
        const externalId = cols.externalId ? String(row[cols.externalId]).trim() : null;

        if (!entryPrice || !quantity || !entryTime) {
          errors.push({
            row: idx + 2,
            message: `Missing required fields for ${symbol}`,
          });
          return;
        }

        trades.push({
          userId,
          externalId: externalId ? `generic_${externalId}` : null,
          broker: 'other',
          accountId: null,
          symbol,
          assetClass: detectAssetClass(symbol),
          direction,
          status: exitTime && exitPrice ? 'closed' : 'open',
          entryPrice,
          exitPrice,
          stopLoss: sl,
          takeProfit: tp,
          quantity: Math.abs(quantity),
          lotSize: null,
          entryTime,
          exitTime,
          pnl,
          pnlPercent:
            pnl !== null && entryPrice && quantity
              ? (pnl / (entryPrice * Math.abs(quantity))) * 100
              : null,
          commission: Math.abs(commission),
          swap,
          currency: ['USD', 'EUR', 'GBP', 'JPY', 'RON', 'CHF', 'AUD', 'CAD'].includes(currency)
            ? (currency as 'USD' | 'EUR' | 'GBP' | 'JPY' | 'RON' | 'CHF' | 'AUD' | 'CAD')
            : 'USD',
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
