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
 * Binance Spot Trade History CSV
 * Export from: binance.com → Wallet → Order History → Spot Order → Export
 *
 * Expected columns (exact names from Binance export):
 * Date(UTC) | Pair | Side | Price | Executed | Amount | Fee
 */
export const binanceImporter: BrokerImporter = {
  broker: 'binance',
  displayName: 'Binance Spot',
  fileTypes: ['csv'],
  description: {
    ro: 'Exportă din Binance: Wallet → Order History → Spot Order → Export',
    en: 'Export from Binance: Wallet → Order History → Spot Order → Export',
  },

  detectSignature: (content: string | Buffer) => {
    const text = typeof content === 'string' ? content : content.toString('utf-8');
    const firstLine = text.split('\n')[0].toLowerCase();
    // Binance exports have "date(utc)" and "pair" and "side"
    return (
      firstLine.includes('date(utc)') ||
      (firstLine.includes('pair') && firstLine.includes('side') && firstLine.includes('executed'))
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

    if (parsed.errors.length > 0) {
      parsed.errors.slice(0, 5).forEach((e) => {
        errors.push({ row: e.row ?? 0, message: e.message });
      });
    }

    const rows = parsed.data;

    // Binance gives us FILLS, not trades. Group by symbol to reconstruct round-trips.
    // For MVP: treat each BUY as an entry, each SELL as an exit.
    // Group fills by pair, then FIFO-match.
    const fillsByPair = new Map<string, RawTradeRow[]>();

    rows.forEach((row, idx) => {
      try {
        const pair = String(row['Pair'] || row['pair'] || '').trim();
        if (!pair) return;

        if (!fillsByPair.has(pair)) fillsByPair.set(pair, []);
        fillsByPair.get(pair)!.push({ ...row, _rowIdx: idx });
      } catch (err) {
        errors.push({ row: idx + 2, message: String(err) });
      }
    });

    // For each pair, FIFO-match buys with sells
    fillsByPair.forEach((fills, pair) => {
      // Sort by date asc
      fills.sort((a, b) => {
        const da = parseDate(String(a['Date(UTC)'] || a['date(utc)'] || ''));
        const db = parseDate(String(b['Date(UTC)'] || b['date(utc)'] || ''));
        if (!da || !db) return 0;
        return da.getTime() - db.getTime();
      });

      const openBuys: Array<{
        quantity: number;
        price: number;
        date: Date;
        fee: number;
        rowIdx: number;
      }> = [];

      fills.forEach((fill) => {
        const rowIdx = (fill._rowIdx as number) ?? 0;
        try {
          const side = String(fill['Side'] || fill['side'] || '').toUpperCase();
          const price = parseNumber(fill['Price'] || fill['price']);
          const executed = String(fill['Executed'] || fill['executed'] || '');
          // "Executed" is like "0.5 BTC" - extract number
          const quantity = parseNumber(executed.match(/[\d.,]+/)?.[0] || '0');
          const dateStr = String(fill['Date(UTC)'] || fill['date(utc)'] || '');
          const date = parseDate(dateStr);
          const feeStr = String(fill['Fee'] || fill['fee'] || '0');
          const fee = parseNumber(feeStr.match(/[\d.,]+/)?.[0] || '0') ?? 0;

          if (!price || !quantity || !date) {
            errors.push({
              row: rowIdx + 2,
              message: `Missing required fields in ${pair}`,
            });
            return;
          }

          if (side === 'BUY') {
            openBuys.push({ quantity, price, date, fee, rowIdx });
          } else if (side === 'SELL') {
            // Match against open buys (FIFO)
            let remainingSell = quantity;
            let totalSellFee = fee;

            while (remainingSell > 0 && openBuys.length > 0) {
              const buy = openBuys[0];
              const matchQty = Math.min(remainingSell, buy.quantity);

              const entryPrice = buy.price;
              const exitPrice = price;
              const pnl = (exitPrice - entryPrice) * matchQty - buy.fee * (matchQty / buy.quantity) - totalSellFee * (matchQty / quantity);

              trades.push({
                userId,
                externalId: `binance_${pair}_${buy.date.toISOString()}_${date.toISOString()}`,
                broker: 'binance',
                accountId: null,
                symbol: pair,
                assetClass: 'crypto',
                direction: 'long',
                status: 'closed',
                entryPrice,
                exitPrice,
                stopLoss: null,
                takeProfit: null,
                quantity: matchQty,
                lotSize: null,
                entryTime: buy.date,
                exitTime: date,
                pnl,
                pnlPercent: ((exitPrice - entryPrice) / entryPrice) * 100,
                commission: buy.fee * (matchQty / buy.quantity) + totalSellFee * (matchQty / quantity),
                swap: 0,
                currency: 'USD',
                rMultiple: null,
                strategy: null,
                tags: [],
                notes: null,
                screenshots: [],
                importSource: file.name,
                importBatch: null,
              });

              buy.quantity -= matchQty;
              remainingSell -= matchQty;
              if (buy.quantity <= 0) openBuys.shift();
            }

            if (remainingSell > 0) {
              errors.push({
                row: rowIdx + 2,
                message: `SELL of ${remainingSell} ${pair} without matching BUY (possibly from before export period)`,
              });
            }
          }
        } catch (err) {
          errors.push({ row: rowIdx + 2, message: String(err) });
        }
      });

      // Leftover buys = open positions
      openBuys.forEach((buy) => {
        trades.push({
          userId,
          externalId: `binance_${pair}_${buy.date.toISOString()}_open`,
          broker: 'binance',
          accountId: null,
          symbol: pair,
          assetClass: 'crypto',
          direction: 'long',
          status: 'open',
          entryPrice: buy.price,
          exitPrice: null,
          stopLoss: null,
          takeProfit: null,
          quantity: buy.quantity,
          lotSize: null,
          entryTime: buy.date,
          exitTime: null,
          pnl: null,
          pnlPercent: null,
          commission: buy.fee,
          swap: 0,
          currency: 'USD',
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
