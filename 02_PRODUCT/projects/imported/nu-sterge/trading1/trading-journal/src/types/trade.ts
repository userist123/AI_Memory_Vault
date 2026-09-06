import { z } from 'zod';

export const TradeDirectionSchema = z.enum(['long', 'short']);
export type TradeDirection = z.infer<typeof TradeDirectionSchema>;

export const TradeStatusSchema = z.enum(['open', 'closed', 'pending', 'cancelled']);
export type TradeStatus = z.infer<typeof TradeStatusSchema>;

export const AssetClassSchema = z.enum([
  'forex',
  'stocks',
  'crypto',
  'futures',
  'options',
  'indices',
  'commodities',
  'etf',
  'cfd',
  'other',
]);
export type AssetClass = z.infer<typeof AssetClassSchema>;

export const CurrencySchema = z.enum(['RON', 'EUR', 'USD', 'GBP', 'JPY', 'CHF', 'AUD', 'CAD']);
export type Currency = z.infer<typeof CurrencySchema>;

export const BrokerSchema = z.enum([
  'mt5',
  'mt4',
  'trading212',
  'binance',
  'ibkr',
  'xtb',
  'etoro',
  'ctrader',
  'alpaca',
  'tradeville',
  'bt_capital',
  'manual',
  'other',
]);
export type Broker = z.infer<typeof BrokerSchema>;

export const TradeSchema = z.object({
  _id: z.string().optional(),
  userId: z.string(),

  // Core identification
  externalId: z.string().nullable().optional().describe('Broker trade ID for dedup'),
  broker: BrokerSchema,
  accountId: z.string().nullable().optional(),

  // Instrument
  symbol: z.string().min(1),
  assetClass: AssetClassSchema,

  // Direction & status
  direction: TradeDirectionSchema,
  status: TradeStatusSchema,

  // Prices
  entryPrice: z.number(),
  exitPrice: z.number().nullable().optional(),
  stopLoss: z.number().nullable().optional(),
  takeProfit: z.number().nullable().optional(),

  // Size
  quantity: z.number().positive(),
  lotSize: z.number().nullable().optional().describe('Forex lots if applicable'),

  // Times (all UTC)
  entryTime: z.date(),
  exitTime: z.date().nullable().optional(),

  // P&L
  pnl: z.number().nullable().optional().describe('Realized P&L in account currency'),
  pnlPercent: z.number().nullable().optional(),
  commission: z.number().default(0),
  swap: z.number().default(0),
  currency: CurrencySchema.default('USD'),

  // Risk
  rMultiple: z.number().nullable().optional(),

  // Metadata
  strategy: z.string().nullable().optional(),
  tags: z.array(z.string()).default([]),
  notes: z.string().nullable().optional(),
  screenshots: z.array(z.string()).default([]),

  // Timestamps
  createdAt: z.date(),
  updatedAt: z.date(),

  // Import tracking
  importSource: z.string().nullable().optional().describe('File name or API source'),
  importBatch: z.string().nullable().optional().describe('Batch ID for rollback'),
});

export type Trade = z.infer<typeof TradeSchema>;

// Raw parsed row before normalization
export interface RawTradeRow {
  [key: string]: string | number | undefined;
}

// Result of import operation
export interface ImportResult {
  success: boolean;
  broker: Broker;
  fileName: string;
  totalRows: number;
  parsedRows: number;
  importedTrades: number;
  duplicates: number;
  errors: ImportError[];
  trades: Trade[];
  batchId: string;
}

export interface ImportError {
  row: number;
  message: string;
  data?: unknown;
}

// Interface each broker importer must implement
export interface BrokerImporter {
  broker: Broker;
  displayName: string;
  fileTypes: string[]; // e.g. ['csv', 'html', 'xlsx']
  description: { ro: string; en: string };
  detectSignature: (content: string | Buffer) => boolean;
  parse: (file: File, userId: string) => Promise<ParsedImport>;
}

export interface ParsedImport {
  trades: Omit<Trade, '_id' | 'createdAt' | 'updatedAt'>[];
  errors: ImportError[];
  totalRows: number;
}
