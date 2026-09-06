import type { Candle } from '@/lib/signals/indicators';

/**
 * Fetch candles (klines) from Binance public API.
 * No API key required. Rate limit 1200/min per IP.
 */

export type Timeframe = '1m' | '5m' | '15m' | '1h' | '4h' | '1d' | '1w';

export async function fetchBinanceCandles(
  symbol: string,
  timeframe: Timeframe,
  limit = 200
): Promise<Candle[]> {
  const url = `https://api.binance.com/api/v3/klines?symbol=${symbol}&interval=${timeframe}&limit=${limit}`;
  const res = await fetch(url, {
    // Cache candles for the timeframe duration
    next: { revalidate: timeframeToSeconds(timeframe) },
  });

  if (!res.ok) {
    throw new Error(`Binance klines: ${res.status}`);
  }

  const raw = (await res.json()) as Array<[
    number, // open time
    string, // open
    string, // high
    string, // low
    string, // close
    string, // volume
    number, // close time
    string, // quote asset volume
    number, // number of trades
    string, // taker buy base
    string, // taker buy quote
    string  // ignore
  ]>;

  return raw.map((k) => ({
    time: k[0],
    open: parseFloat(k[1]),
    high: parseFloat(k[2]),
    low: parseFloat(k[3]),
    close: parseFloat(k[4]),
    volume: parseFloat(k[5]),
  }));
}

function timeframeToSeconds(tf: Timeframe): number {
  switch (tf) {
    case '1m': return 60;
    case '5m': return 300;
    case '15m': return 900;
    case '1h': return 3600;
    case '4h': return 14400;
    case '1d': return 86400;
    case '1w': return 604800;
  }
}

/**
 * Get top traded crypto pairs (for scanner).
 */
export async function getTopBinancePairs(limit = 20, quote = 'USDT'): Promise<string[]> {
  const res = await fetch('https://api.binance.com/api/v3/ticker/24hr', {
    next: { revalidate: 300 }, // 5 min cache
  });
  if (!res.ok) throw new Error('Failed to fetch 24hr tickers');

  const tickers = (await res.json()) as Array<{
    symbol: string;
    quoteVolume: string;
    count: number;
  }>;

  return tickers
    .filter((t) => t.symbol.endsWith(quote))
    .sort((a, b) => parseFloat(b.quoteVolume) - parseFloat(a.quoteVolume))
    .slice(0, limit)
    .map((t) => t.symbol);
}
