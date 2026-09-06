import { parse } from 'node-html-parser';
import type {
  BrokerImporter,
  ParsedImport,
  Trade,
  ImportError,
} from '@/types/trade';
import { detectAssetClass, parseNumber, parseDate } from './utils';

/**
 * MetaTrader 5 HTML Report
 * Export from MT5: Toolbox → History → right-click → Report → HTML
 *
 * The "Positions" table has columns:
 * Time | Position | Symbol | Type | Volume | Price | S/L | T/P | Time | Price | Commission | Swap | Profit
 */
export const mt5Importer: BrokerImporter = {
  broker: 'mt5',
  displayName: 'MetaTrader 5',
  fileTypes: ['html', 'htm'],
  description: {
    ro: 'Exportă din MT5: Toolbox → Istoric → click dreapta → Raport → HTML',
    en: 'Export from MT5: Toolbox → History → right-click → Report → HTML',
  },

  detectSignature: (content: string | Buffer) => {
    const text = typeof content === 'string' ? content : content.toString('utf-8');
    const lower = text.toLowerCase();
    return (
      (lower.includes('metatrader') || lower.includes('metaquotes')) &&
      lower.includes('position') &&
      (lower.includes('<table') || lower.includes('<tr'))
    );
  },

  parse: async (file: File, userId: string): Promise<ParsedImport> => {
    const html = await file.text();
    const errors: ImportError[] = [];
    const trades: Omit<Trade, '_id' | 'createdAt' | 'updatedAt'>[] = [];

    const root = parse(html);
    const tables = root.querySelectorAll('table');

    // Find the Positions table by looking for header row
    let positionsTable: ReturnType<typeof root.querySelector> = null;
    for (const table of tables) {
      const firstRows = table.querySelectorAll('tr').slice(0, 5);
      const text = firstRows.map((r) => r.text.toLowerCase()).join(' ');
      if (
        (text.includes('position') || text.includes('positions')) &&
        text.includes('symbol') &&
        (text.includes('profit') || text.includes('p/l'))
      ) {
        positionsTable = table;
        break;
      }
    }

    if (!positionsTable) {
      errors.push({
        row: 0,
        message: 'Could not find Positions table in MT5 report. Make sure you exported as HTML from Toolbox → History.',
      });
      return { trades: [], errors, totalRows: 0 };
    }

    const rows = positionsTable.querySelectorAll('tr');
    let headerRowIdx = -1;

    // Find header row - the one that contains "Symbol" and "Profit"
    for (let i = 0; i < rows.length; i++) {
      const text = rows[i].text.toLowerCase();
      if (text.includes('symbol') && (text.includes('profit') || text.includes('p/l'))) {
        headerRowIdx = i;
        break;
      }
    }

    if (headerRowIdx === -1) {
      errors.push({ row: 0, message: 'Could not find header row in Positions table' });
      return { trades: [], errors, totalRows: 0 };
    }

    const headers = rows[headerRowIdx]
      .querySelectorAll('td, th')
      .map((c) => c.text.trim().toLowerCase());

    // Map column indices (handles MT5's duplicated "Time" and "Price" columns)
    const findCol = (keywords: string[]): number => {
      for (let i = 0; i < headers.length; i++) {
        for (const kw of keywords) {
          if (headers[i].includes(kw.toLowerCase())) return i;
        }
      }
      return -1;
    };

    // MT5 has: Time | Position | Symbol | Type | Volume | Price | S/L | T/P | Time | Price | Commission | Swap | Profit
    //          0      1          2        3      4        5        6     7     8      9        10           11     12
    const colTimeEntry = 0;
    const colPositionId = 1;
    const colSymbol = 2;
    const colType = 3;
    const colVolume = 4;
    const colEntryPrice = 5;
    const colSL = 6;
    const colTP = 7;
    const colTimeExit = 8;
    const colExitPrice = 9;
    const colCommission = findCol(['commission']);
    const colSwap = findCol(['swap']);
    const colProfit = findCol(['profit', 'p/l']);

    for (let i = headerRowIdx + 1; i < rows.length; i++) {
      const cells = rows[i].querySelectorAll('td');
      if (cells.length < 10) continue; // Skip summary rows

      try {
        const symbol = cells[colSymbol]?.text.trim() || '';
        if (!symbol || symbol.toLowerCase().includes('balance')) continue;

        const type = (cells[colType]?.text.trim() || '').toLowerCase();
        if (!['buy', 'sell'].includes(type)) continue;

        const entryTime = parseDate(cells[colTimeEntry]?.text.trim() || '');
        const exitTime = parseDate(cells[colTimeExit]?.text.trim() || '');
        const volume = parseNumber(cells[colVolume]?.text.trim() || '');
        const entryPrice = parseNumber(cells[colEntryPrice]?.text.trim() || '');
        const exitPrice = parseNumber(cells[colExitPrice]?.text.trim() || '');
        const sl = parseNumber(cells[colSL]?.text.trim() || '');
        const tp = parseNumber(cells[colTP]?.text.trim() || '');
        const commission =
          colCommission >= 0 ? parseNumber(cells[colCommission]?.text.trim() || '0') ?? 0 : 0;
        const swap =
          colSwap >= 0 ? parseNumber(cells[colSwap]?.text.trim() || '0') ?? 0 : 0;
        const profit =
          colProfit >= 0 ? parseNumber(cells[colProfit]?.text.trim() || '0') : null;

        if (!entryTime || !volume || !entryPrice) {
          errors.push({
            row: i + 1,
            message: `Incomplete row for ${symbol}`,
          });
          continue;
        }

        const positionId = cells[colPositionId]?.text.trim() || null;

        trades.push({
          userId,
          externalId: positionId ? `mt5_${positionId}` : null,
          broker: 'mt5',
          accountId: null,
          symbol,
          assetClass: detectAssetClass(symbol),
          direction: type === 'buy' ? 'long' : 'short',
          status: exitTime && exitPrice ? 'closed' : 'open',
          entryPrice,
          exitPrice: exitPrice || null,
          stopLoss: sl,
          takeProfit: tp,
          quantity: volume,
          lotSize: volume, // Forex lots
          entryTime,
          exitTime: exitTime || null,
          pnl: profit,
          pnlPercent: null,
          commission: Math.abs(commission),
          swap,
          currency: 'USD',
          rMultiple: null,
          strategy: null,
          tags: [],
          notes: null,
          screenshots: [],
          importSource: file.name,
          importBatch: null,
        });
      } catch (err) {
        errors.push({ row: i + 1, message: String(err) });
      }
    }

    return {
      trades,
      errors,
      totalRows: rows.length - headerRowIdx - 1,
    };
  },
};
