import { z } from 'zod';

/**
 * Universal broker interface - all brokers implement these methods.
 * Allows swapping brokers without changing business logic.
 */

export const BrokerIdSchema = z.enum(['binance', 'alpaca', 'ibkr']);
export type BrokerId = z.infer<typeof BrokerIdSchema>;

export const OrderSideSchema = z.enum(['buy', 'sell']);
export type OrderSide = z.infer<typeof OrderSideSchema>;

export const OrderTypeSchema = z.enum(['market', 'limit', 'stop', 'stop_limit']);
export type OrderType = z.infer<typeof OrderTypeSchema>;

export const OrderStatusSchema = z.enum([
  'pending', 'filled', 'partially_filled', 'canceled', 'rejected', 'expired',
]);
export type OrderStatus = z.infer<typeof OrderStatusSchema>;

// Encrypted credentials stored in DB
export interface BrokerCredentials {
  brokerId: BrokerId;
  userId: string;
  // Encrypted with AES-256-GCM using server key
  encryptedApiKey: string;
  encryptedApiSecret: string;
  // Optional extras per broker
  encryptedExtras?: Record<string, string>;
  // Testnet vs production
  testnet: boolean;
  // Metadata
  label?: string;
  createdAt: Date;
  lastUsedAt?: Date;
  // Permissions granted (for display to user)
  permissions: string[];
}

// Generic order request
export const OrderRequestSchema = z.object({
  symbol: z.string(),
  side: OrderSideSchema,
  type: OrderTypeSchema,
  quantity: z.number().positive(),
  price: z.number().positive().optional(), // for limit orders
  stopPrice: z.number().positive().optional(), // for stop orders
  // Client-provided ID for dedup (idempotency)
  clientOrderId: z.string().optional(),
  // Additional per-trade safety
  reduceOnly: z.boolean().optional(),
  timeInForce: z.enum(['GTC', 'IOC', 'FOK']).optional(),
});

export type OrderRequest = z.infer<typeof OrderRequestSchema>;

export interface PlacedOrder {
  brokerOrderId: string;
  clientOrderId?: string;
  symbol: string;
  side: OrderSide;
  type: OrderType;
  quantity: number;
  filledQuantity: number;
  price?: number;
  avgFillPrice?: number;
  status: OrderStatus;
  submittedAt: Date;
  filledAt?: Date;
  commission?: number;
  commissionAsset?: string;
  rawResponse?: unknown;
}

export interface AccountBalance {
  currency: string;
  free: number;
  locked: number;
  total: number;
}

export interface Position {
  symbol: string;
  side: 'long' | 'short';
  quantity: number;
  avgEntryPrice: number;
  currentPrice: number;
  unrealizedPnl: number;
  unrealizedPnlPct: number;
}

export interface BrokerInfo {
  id: BrokerId;
  displayName: string;
  supportedAssets: ('crypto' | 'stocks' | 'forex' | 'futures')[];
  testnetAvailable: boolean;
  docsUrl: string;
  apiKeyUrl: string; // where user creates API key
  requiredPermissions: string[];
  recommendedPermissions: string[];
}

/**
 * Every broker adapter implements this interface.
 * Business logic never depends on specific broker - always on this contract.
 */
export interface BrokerAdapter {
  readonly info: BrokerInfo;

  // Credentials
  validateCredentials(credentials: Omit<BrokerCredentials, 'userId' | 'createdAt' | 'permissions' | 'encryptedApiKey' | 'encryptedApiSecret'> & { apiKey: string; apiSecret: string }): Promise<{
    valid: boolean;
    permissions: string[];
    accountType?: string;
    error?: string;
  }>;

  // Account
  getBalances(credentials: { apiKey: string; apiSecret: string; testnet: boolean }): Promise<AccountBalance[]>;
  getPositions(credentials: { apiKey: string; apiSecret: string; testnet: boolean }): Promise<Position[]>;

  // Orders
  placeOrder(
    credentials: { apiKey: string; apiSecret: string; testnet: boolean },
    order: OrderRequest
  ): Promise<PlacedOrder>;

  getOrder(
    credentials: { apiKey: string; apiSecret: string; testnet: boolean },
    brokerOrderId: string,
    symbol?: string
  ): Promise<PlacedOrder | null>;

  cancelOrder(
    credentials: { apiKey: string; apiSecret: string; testnet: boolean },
    brokerOrderId: string,
    symbol?: string
  ): Promise<boolean>;

  // Market data (bypasses credentials for public endpoints)
  getCurrentPrice(symbol: string): Promise<number>;
  getSymbolInfo(symbol: string): Promise<{
    minQty: number;
    maxQty: number;
    stepSize: number;
    minNotional: number;
    tickSize: number;
  } | null>;
}
