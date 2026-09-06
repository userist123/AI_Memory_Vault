import type {
  BrokerAdapter,
  OrderRequest,
  PlacedOrder,
  AccountBalance,
  Position,
} from './types';

const ALPACA_PROD_URL = 'https://api.alpaca.markets';
const ALPACA_PAPER_URL = 'https://paper-api.alpaca.markets';

interface Creds {
  apiKey: string;
  apiSecret: string;
  testnet: boolean;
}

async function alpacaRequest(
  creds: Creds,
  method: string,
  path: string,
  body?: unknown
): Promise<unknown> {
  const baseUrl = creds.testnet ? ALPACA_PAPER_URL : ALPACA_PROD_URL;
  const res = await fetch(`${baseUrl}${path}`, {
    method,
    headers: {
      'APCA-API-KEY-ID': creds.apiKey,
      'APCA-API-SECRET-KEY': creds.apiSecret,
      'Content-Type': 'application/json',
    },
    body: body ? JSON.stringify(body) : undefined,
  });

  const text = await res.text();
  let parsed: unknown;
  try { parsed = JSON.parse(text); } catch { parsed = text; }

  if (!res.ok) {
    const err = parsed as { message?: string };
    throw new Error(`Alpaca ${res.status}: ${err.message || text}`);
  }

  return parsed;
}

export const alpacaAdapter: BrokerAdapter = {
  info: {
    id: 'alpaca',
    displayName: 'Alpaca (US Stocks)',
    supportedAssets: ['stocks'],
    testnetAvailable: true,
    docsUrl: 'https://alpaca.markets/docs/',
    apiKeyUrl: 'https://app.alpaca.markets/paper/dashboard/overview',
    requiredPermissions: ['Trading API'],
    recommendedPermissions: ['Paper Trading (for testing)'],
  },

  async validateCredentials({ apiKey, apiSecret, testnet }) {
    try {
      const account = (await alpacaRequest({ apiKey, apiSecret, testnet }, 'GET', '/v2/account')) as {
        status: string;
        trading_blocked: boolean;
        account_blocked: boolean;
      };
      return {
        valid: account.status === 'ACTIVE' && !account.trading_blocked && !account.account_blocked,
        permissions: ['stock_trading'],
        accountType: testnet ? 'paper' : 'live',
      };
    } catch (err: unknown) {
      const e = err as { message?: string };
      return { valid: false, permissions: [], error: e.message };
    }
  },

  async getBalances(creds) {
    const acc = (await alpacaRequest(creds, 'GET', '/v2/account')) as {
      cash: string;
      currency: string;
      buying_power: string;
    };
    return [{
      currency: acc.currency || 'USD',
      free: parseFloat(acc.cash),
      locked: 0,
      total: parseFloat(acc.buying_power),
    }];
  },

  async getPositions(creds) {
    const positions = (await alpacaRequest(creds, 'GET', '/v2/positions')) as Array<{
      symbol: string;
      qty: string;
      avg_entry_price: string;
      current_price: string;
      unrealized_pl: string;
      unrealized_plpc: string;
      side: string;
    }>;
    return positions.map((p) => ({
      symbol: p.symbol,
      side: p.side === 'long' ? 'long' : 'short' as 'long' | 'short',
      quantity: parseFloat(p.qty),
      avgEntryPrice: parseFloat(p.avg_entry_price),
      currentPrice: parseFloat(p.current_price),
      unrealizedPnl: parseFloat(p.unrealized_pl),
      unrealizedPnlPct: parseFloat(p.unrealized_plpc) * 100,
    }));
  },

  async placeOrder(creds, order) {
    const body: Record<string, unknown> = {
      symbol: order.symbol,
      side: order.side,
      type: order.type === 'stop_limit' ? 'stop_limit' : order.type,
      qty: order.quantity,
      time_in_force: order.timeInForce?.toLowerCase() || 'day',
    };
    if (order.price) body.limit_price = order.price;
    if (order.stopPrice) body.stop_price = order.stopPrice;
    if (order.clientOrderId) body.client_order_id = order.clientOrderId;

    const response = (await alpacaRequest(creds, 'POST', '/v2/orders', body)) as {
      id: string;
      client_order_id: string;
      symbol: string;
      side: string;
      type: string;
      qty: string;
      filled_qty: string;
      filled_avg_price: string | null;
      limit_price: string | null;
      stop_price: string | null;
      status: string;
      submitted_at: string;
      filled_at: string | null;
    };

    return {
      brokerOrderId: response.id,
      clientOrderId: response.client_order_id,
      symbol: response.symbol,
      side: order.side,
      type: order.type,
      quantity: parseFloat(response.qty),
      filledQuantity: parseFloat(response.filled_qty || '0'),
      price: response.limit_price ? parseFloat(response.limit_price) : undefined,
      avgFillPrice: response.filled_avg_price ? parseFloat(response.filled_avg_price) : undefined,
      status: response.status === 'filled' ? 'filled' :
              response.status === 'partially_filled' ? 'partially_filled' :
              response.status === 'canceled' ? 'canceled' :
              response.status === 'rejected' ? 'rejected' :
              response.status === 'expired' ? 'expired' : 'pending',
      submittedAt: new Date(response.submitted_at),
      filledAt: response.filled_at ? new Date(response.filled_at) : undefined,
      rawResponse: response,
    };
  },

  async getOrder(creds, brokerOrderId) {
    const response = (await alpacaRequest(creds, 'GET', `/v2/orders/${brokerOrderId}`)) as {
      id: string;
      symbol: string;
      side: string;
      type: string;
      qty: string;
      filled_qty: string;
      filled_avg_price: string | null;
      limit_price: string | null;
      status: string;
      submitted_at: string;
      filled_at: string | null;
    };
    return {
      brokerOrderId: response.id,
      symbol: response.symbol,
      side: response.side as 'buy' | 'sell',
      type: response.type as 'market' | 'limit' | 'stop' | 'stop_limit',
      quantity: parseFloat(response.qty),
      filledQuantity: parseFloat(response.filled_qty || '0'),
      price: response.limit_price ? parseFloat(response.limit_price) : undefined,
      avgFillPrice: response.filled_avg_price ? parseFloat(response.filled_avg_price) : undefined,
      status: response.status === 'filled' ? 'filled' :
              response.status === 'partially_filled' ? 'partially_filled' :
              response.status === 'canceled' ? 'canceled' :
              response.status === 'rejected' ? 'rejected' :
              response.status === 'expired' ? 'expired' : 'pending',
      submittedAt: new Date(response.submitted_at),
      filledAt: response.filled_at ? new Date(response.filled_at) : undefined,
    };
  },

  async cancelOrder(creds, brokerOrderId) {
    try {
      await alpacaRequest(creds, 'DELETE', `/v2/orders/${brokerOrderId}`);
      return true;
    } catch {
      return false;
    }
  },

  async getCurrentPrice(symbol) {
    // Alpaca market data requires data subscription, use public Yahoo finance as fallback
    const res = await fetch(`https://query1.finance.yahoo.com/v8/finance/chart/${symbol}?interval=1m&range=1d`);
    const data = await res.json() as { chart?: { result?: Array<{ meta?: { regularMarketPrice?: number } }> } };
    const price = data.chart?.result?.[0]?.meta?.regularMarketPrice;
    if (!price) throw new Error(`No price for ${symbol}`);
    return price;
  },

  async getSymbolInfo(_symbol) {
    // Alpaca doesn't have strict lot size filters like Binance - fractional shares allowed
    return { minQty: 0.001, maxQty: 1e9, stepSize: 0.001, minNotional: 1, tickSize: 0.01 };
  },
};
