import type {
  BrokerAdapter,
  BrokerInfo,
  OrderRequest,
  PlacedOrder,
  AccountBalance,
  Position,
} from './types';

const BINANCE_PROD_URL = 'https://api.binance.com';
const BINANCE_TESTNET_URL = 'https://testnet.binance.vision';

interface Creds {
  apiKey: string;
  apiSecret: string;
  testnet: boolean;
}

/**
 * Sign a query string using HMAC-SHA256 (Binance signature requirement).
 * Uses Web Crypto API - works on CF Workers + Node.
 */
async function signQuery(secret: string, queryString: string): Promise<string> {
  const key = await crypto.subtle.importKey(
    'raw',
    new TextEncoder().encode(secret),
    { name: 'HMAC', hash: 'SHA-256' },
    false,
    ['sign']
  );
  const signature = await crypto.subtle.sign(
    'HMAC',
    key,
    new TextEncoder().encode(queryString)
  );
  const bytes = new Uint8Array(signature);
  return Array.from(bytes).map((b) => b.toString(16).padStart(2, '0')).join('');
}

/**
 * Make a signed request to Binance.
 */
async function signedRequest(
  creds: Creds,
  method: 'GET' | 'POST' | 'DELETE',
  path: string,
  params: Record<string, string | number | boolean> = {}
): Promise<unknown> {
  const baseUrl = creds.testnet ? BINANCE_TESTNET_URL : BINANCE_PROD_URL;
  const timestamp = Date.now();

  const queryParams: Record<string, string> = {};
  for (const [k, v] of Object.entries(params)) {
    queryParams[k] = String(v);
  }
  queryParams.timestamp = String(timestamp);
  queryParams.recvWindow = '5000';

  const queryString = new URLSearchParams(queryParams).toString();
  const signature = await signQuery(creds.apiSecret, queryString);

  const url = `${baseUrl}${path}?${queryString}&signature=${signature}`;

  const res = await fetch(url, {
    method,
    headers: {
      'X-MBX-APIKEY': creds.apiKey,
      'Content-Type': 'application/json',
    },
  });

  const body = await res.text();
  let parsed: unknown;
  try {
    parsed = JSON.parse(body);
  } catch {
    parsed = body;
  }

  if (!res.ok) {
    const err = parsed as { msg?: string; code?: number };
    throw new Error(`Binance ${res.status}: ${err.msg || body} (code ${err.code ?? 'N/A'})`);
  }

  return parsed;
}

/**
 * Public request - no signing required.
 */
async function publicRequest(
  testnet: boolean,
  path: string,
  params: Record<string, string | number> = {}
): Promise<unknown> {
  const baseUrl = testnet ? BINANCE_TESTNET_URL : BINANCE_PROD_URL;
  const queryString = new URLSearchParams(
    Object.entries(params).map(([k, v]) => [k, String(v)])
  ).toString();
  const url = queryString ? `${baseUrl}${path}?${queryString}` : `${baseUrl}${path}`;
  const res = await fetch(url);
  if (!res.ok) {
    throw new Error(`Binance public ${res.status}: ${await res.text()}`);
  }
  return res.json();
}

// Cache exchange info (symbols + filters) - slow to fetch, rarely changes
let cachedExchangeInfo: {
  data: Record<string, SymbolFilters>;
  fetchedAt: number;
  testnet: boolean;
} | null = null;

interface SymbolFilters {
  minQty: number;
  maxQty: number;
  stepSize: number;
  minNotional: number;
  tickSize: number;
  status: string;
}

async function getExchangeInfo(testnet: boolean): Promise<Record<string, SymbolFilters>> {
  // Cache 1 hour
  if (
    cachedExchangeInfo &&
    cachedExchangeInfo.testnet === testnet &&
    Date.now() - cachedExchangeInfo.fetchedAt < 3600000
  ) {
    return cachedExchangeInfo.data;
  }

  const info = (await publicRequest(testnet, '/api/v3/exchangeInfo')) as {
    symbols: Array<{
      symbol: string;
      status: string;
      filters: Array<{ filterType: string; [k: string]: unknown }>;
    }>;
  };

  const map: Record<string, SymbolFilters> = {};
  for (const s of info.symbols) {
    if (s.status !== 'TRADING') continue;
    const lotSize = s.filters.find((f) => f.filterType === 'LOT_SIZE') as Record<string, string> | undefined;
    const priceFilter = s.filters.find((f) => f.filterType === 'PRICE_FILTER') as Record<string, string> | undefined;
    const notional = s.filters.find((f) => f.filterType === 'NOTIONAL' || f.filterType === 'MIN_NOTIONAL') as Record<string, string> | undefined;

    map[s.symbol] = {
      minQty: parseFloat(lotSize?.minQty || '0'),
      maxQty: parseFloat(lotSize?.maxQty || '0'),
      stepSize: parseFloat(lotSize?.stepSize || '0'),
      minNotional: parseFloat(notional?.minNotional || '0'),
      tickSize: parseFloat(priceFilter?.tickSize || '0'),
      status: s.status,
    };
  }

  cachedExchangeInfo = { data: map, fetchedAt: Date.now(), testnet };
  return map;
}

/**
 * Round quantity to Binance's stepSize.
 * If you send 0.123456 but stepSize is 0.001, you get rejected.
 */
function roundToStep(value: number, step: number): number {
  if (step === 0) return value;
  return Math.floor(value / step) * step;
}

export const binanceAdapter: BrokerAdapter = {
  info: {
    id: 'binance',
    displayName: 'Binance',
    supportedAssets: ['crypto'],
    testnetAvailable: true,
    docsUrl: 'https://binance-docs.github.io/apidocs/spot/en/',
    apiKeyUrl: 'https://www.binance.com/en/my/settings/api-management',
    requiredPermissions: ['Enable Reading'],
    recommendedPermissions: ['Enable Spot & Margin Trading'],
  },

  async validateCredentials({ apiKey, apiSecret, testnet }) {
    try {
      const account = (await signedRequest(
        { apiKey, apiSecret, testnet },
        'GET',
        '/api/v3/account'
      )) as {
        canTrade: boolean;
        canWithdraw: boolean;
        accountType?: string;
        permissions?: string[];
      };

      const permissions: string[] = [];
      if (account.canTrade) permissions.push('spot_trading');
      if (account.canWithdraw) permissions.push('withdraw');
      if (account.permissions) permissions.push(...account.permissions);

      if (account.canWithdraw) {
        return {
          valid: false,
          permissions,
          error: 'WARNING: Withdrawal permission detected. For safety, disable it in Binance API settings.',
        };
      }

      return {
        valid: true,
        permissions,
        accountType: account.accountType,
      };
    } catch (err: unknown) {
      const e = err as { message?: string };
      return { valid: false, permissions: [], error: e.message || 'Validation failed' };
    }
  },

  async getBalances(creds) {
    const account = (await signedRequest(creds, 'GET', '/api/v3/account')) as {
      balances: Array<{ asset: string; free: string; locked: string }>;
    };
    return account.balances
      .map((b) => ({
        currency: b.asset,
        free: parseFloat(b.free),
        locked: parseFloat(b.locked),
        total: parseFloat(b.free) + parseFloat(b.locked),
      }))
      .filter((b) => b.total > 0);
  },

  async getPositions(creds) {
    // Spot doesn't have "positions" - only balances
    // We infer positions from balances that aren't quote currencies
    const balances = await this.getBalances(creds);
    const positions: Position[] = [];

    for (const bal of balances) {
      if (['USDT', 'USDC', 'BUSD', 'DAI', 'TUSD', 'EUR', 'USD'].includes(bal.currency)) continue;
      if (bal.total === 0) continue;

      try {
        const symbol = `${bal.currency}USDT`;
        const currentPrice = await this.getCurrentPrice(symbol);
        positions.push({
          symbol,
          side: 'long',
          quantity: bal.total,
          avgEntryPrice: 0, // Binance doesn't give entry price for spot
          currentPrice,
          unrealizedPnl: 0, // Cannot compute without entry price
          unrealizedPnlPct: 0,
        });
      } catch {
        // Symbol not tradeable, skip
      }
    }

    return positions;
  },

  async placeOrder(creds, order) {
    // Validate against symbol filters FIRST (fail fast, save API call)
    const info = await getExchangeInfo(creds.testnet);
    const filters = info[order.symbol];

    if (!filters) {
      throw new Error(`Symbol ${order.symbol} not found on Binance ${creds.testnet ? 'Testnet' : 'Spot'}`);
    }

    const adjustedQty = roundToStep(order.quantity, filters.stepSize);
    if (adjustedQty < filters.minQty) {
      throw new Error(
        `Quantity ${adjustedQty} below minimum ${filters.minQty} for ${order.symbol}`
      );
    }

    // Check notional (price × qty must be > minNotional)
    const refPrice = order.price ?? (await this.getCurrentPrice(order.symbol));
    const notional = refPrice * adjustedQty;
    if (notional < filters.minNotional) {
      throw new Error(
        `Order value ${notional.toFixed(2)} USDT below minimum ${filters.minNotional} for ${order.symbol}`
      );
    }

    const params: Record<string, string | number> = {
      symbol: order.symbol,
      side: order.side.toUpperCase(),
      type: order.type === 'market' ? 'MARKET' : order.type === 'limit' ? 'LIMIT' : 'STOP_LOSS_LIMIT',
      quantity: adjustedQty,
    };

    if (order.type === 'limit' || order.type === 'stop_limit') {
      if (!order.price) throw new Error('Limit orders require price');
      params.price = roundToStep(order.price, filters.tickSize);
      params.timeInForce = order.timeInForce || 'GTC';
    }

    if (order.type === 'stop' || order.type === 'stop_limit') {
      if (!order.stopPrice) throw new Error('Stop orders require stopPrice');
      params.stopPrice = roundToStep(order.stopPrice, filters.tickSize);
    }

    if (order.clientOrderId) {
      params.newClientOrderId = order.clientOrderId;
    }

    const response = (await signedRequest(creds, 'POST', '/api/v3/order', params)) as {
      orderId: number;
      clientOrderId?: string;
      symbol: string;
      status: string;
      executedQty: string;
      cummulativeQuoteQty?: string;
      fills?: Array<{ price: string; qty: string; commission: string; commissionAsset: string }>;
      transactTime?: number;
    };

    // Aggregate fills for avg price
    let avgFillPrice: number | undefined;
    let totalCommission = 0;
    let commissionAsset: string | undefined;
    if (response.fills && response.fills.length > 0) {
      let totalQuote = 0;
      let totalBase = 0;
      for (const fill of response.fills) {
        totalQuote += parseFloat(fill.price) * parseFloat(fill.qty);
        totalBase += parseFloat(fill.qty);
        totalCommission += parseFloat(fill.commission);
        commissionAsset = fill.commissionAsset;
      }
      avgFillPrice = totalBase > 0 ? totalQuote / totalBase : undefined;
    }

    return {
      brokerOrderId: String(response.orderId),
      clientOrderId: response.clientOrderId,
      symbol: response.symbol,
      side: order.side,
      type: order.type,
      quantity: adjustedQty,
      filledQuantity: parseFloat(response.executedQty),
      price: order.price,
      avgFillPrice,
      status: response.status === 'FILLED' ? 'filled' :
              response.status === 'PARTIALLY_FILLED' ? 'partially_filled' :
              response.status === 'NEW' ? 'pending' :
              response.status === 'CANCELED' ? 'canceled' :
              response.status === 'REJECTED' ? 'rejected' : 'pending',
      submittedAt: new Date(response.transactTime || Date.now()),
      filledAt: response.status === 'FILLED' ? new Date(response.transactTime || Date.now()) : undefined,
      commission: totalCommission,
      commissionAsset,
      rawResponse: response,
    };
  },

  async getOrder(creds, brokerOrderId, symbol) {
    if (!symbol) throw new Error('Binance requires symbol to query order');
    const response = (await signedRequest(creds, 'GET', '/api/v3/order', {
      symbol,
      orderId: brokerOrderId,
    })) as {
      orderId: number;
      symbol: string;
      side: string;
      type: string;
      status: string;
      origQty: string;
      executedQty: string;
      price: string;
      stopPrice?: string;
      time: number;
      updateTime?: number;
    };

    return {
      brokerOrderId: String(response.orderId),
      symbol: response.symbol,
      side: response.side.toLowerCase() as 'buy' | 'sell',
      type: response.type.toLowerCase() as 'market' | 'limit' | 'stop' | 'stop_limit',
      quantity: parseFloat(response.origQty),
      filledQuantity: parseFloat(response.executedQty),
      price: parseFloat(response.price) || undefined,
      status: response.status === 'FILLED' ? 'filled' :
              response.status === 'PARTIALLY_FILLED' ? 'partially_filled' :
              response.status === 'NEW' ? 'pending' :
              response.status === 'CANCELED' ? 'canceled' :
              response.status === 'REJECTED' ? 'rejected' :
              response.status === 'EXPIRED' ? 'expired' : 'pending',
      submittedAt: new Date(response.time),
      filledAt: response.updateTime ? new Date(response.updateTime) : undefined,
    };
  },

  async cancelOrder(creds, brokerOrderId, symbol) {
    if (!symbol) throw new Error('Binance requires symbol to cancel order');
    try {
      await signedRequest(creds, 'DELETE', '/api/v3/order', {
        symbol,
        orderId: brokerOrderId,
      });
      return true;
    } catch (err) {
      console.warn('[Binance] Cancel failed:', err);
      return false;
    }
  },

  async getCurrentPrice(symbol) {
    // Use production endpoint for prices even on testnet
    // (testnet often has stale/missing pairs)
    const res = (await publicRequest(false, '/api/v3/ticker/price', { symbol })) as { price: string };
    return parseFloat(res.price);
  },

  async getSymbolInfo(symbol) {
    const info = await getExchangeInfo(false);
    return info[symbol] || null;
  },
};
