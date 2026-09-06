import type { AssetClass } from '@/types/trade';

// Major forex pairs
const FOREX_BASES = new Set([
  'USD', 'EUR', 'GBP', 'JPY', 'CHF', 'AUD', 'CAD', 'NZD',
  'SEK', 'NOK', 'DKK', 'PLN', 'RON', 'HUF', 'CZK', 'TRY', 'MXN', 'ZAR',
  'SGD', 'HKD', 'CNH', 'CNY',
]);

// Common crypto symbols (base)
const CRYPTO_SYMBOLS = new Set([
  'BTC', 'ETH', 'BNB', 'SOL', 'XRP', 'ADA', 'DOGE', 'DOT', 'AVAX', 'MATIC',
  'LINK', 'UNI', 'ATOM', 'LTC', 'BCH', 'TRX', 'ETC', 'XLM', 'VET', 'FIL',
  'NEAR', 'ALGO', 'ICP', 'APT', 'ARB', 'OP', 'SHIB', 'PEPE', 'BONK',
  'USDT', 'USDC', 'DAI', 'BUSD',
]);

// Common index symbols
const INDEX_SYMBOLS = new Set([
  'SPX', 'SPY', 'QQQ', 'DJI', 'DIA', 'NDX', 'RUT', 'IWM', 'VIX',
  'DAX', 'GER40', 'GER30', 'DE40', 'UK100', 'FTSE', 'NI225', 'N225',
  'US30', 'US500', 'NAS100', 'US100', 'USTEC', 'WSJ30',
  'STOXX50', 'EUSTX50', 'CAC40', 'FRA40', 'IBEX35', 'SPA35',
  'BET', 'BETI', // Romanian index
]);

// Commodities
const COMMODITY_SYMBOLS = new Set([
  'XAUUSD', 'XAGUSD', 'GOLD', 'SILVER', 'XAU', 'XAG',
  'WTI', 'BRENT', 'CL', 'OIL', 'USOIL', 'UKOIL',
  'COPPER', 'HG',
  'NATGAS', 'NG',
]);

/**
 * Detect asset class from symbol. Handles many broker naming conventions.
 */
export function detectAssetClass(symbol: string): AssetClass {
  const s = symbol.toUpperCase().replace(/[._\-/]/g, '');

  // Check commodities first (specific)
  if (COMMODITY_SYMBOLS.has(s) || /^(XAU|XAG|GOLD|SILVER|OIL|WTI|BRENT|NATGAS)/i.test(s)) {
    return 'commodities';
  }

  // Check indices
  if (INDEX_SYMBOLS.has(s)) return 'indices';
  if (/^(US30|US500|NAS100|USTEC|GER40|UK100|JPN225|HK50|BET)/i.test(s)) {
    return 'indices';
  }

  // Crypto: BTC/USD, BTCUSD, BTCUSDT, ETHBTC, etc.
  if (s.length >= 6 && s.length <= 10) {
    const firstThree = s.slice(0, 3);
    const firstFour = s.slice(0, 4);
    if (CRYPTO_SYMBOLS.has(firstThree) || CRYPTO_SYMBOLS.has(firstFour)) {
      // Make sure it's crypto, not forex
      const rest = s.slice(firstThree.length);
      if (rest === 'USDT' || rest === 'USDC' || rest === 'BUSD' || rest === 'DAI') {
        return 'crypto';
      }
      if (CRYPTO_SYMBOLS.has(rest.slice(0, 3)) || CRYPTO_SYMBOLS.has(rest)) {
        return 'crypto';
      }
      // BTCUSD, ETHUSD etc. - still crypto
      if (rest === 'USD' || rest === 'EUR' || rest === 'GBP') {
        return 'crypto';
      }
    }
  }

  // Forex: 6 chars, both halves are currency codes
  if (s.length === 6) {
    const base = s.slice(0, 3);
    const quote = s.slice(3, 6);
    if (FOREX_BASES.has(base) && FOREX_BASES.has(quote)) {
      return 'forex';
    }
  }

  // Futures: contains digits (ES, NQ, CL) or .f suffix
  if (/^[A-Z]{1,3}[HMUZ]\d{1,2}$/.test(s) || symbol.endsWith('.f')) {
    return 'futures';
  }

  // Options: symbol contains expiration/strike pattern
  if (/\d{6}[CP]\d+/.test(symbol) || symbol.includes('CALL') || symbol.includes('PUT')) {
    return 'options';
  }

  // ETFs: common prefixes
  if (/^(SPY|QQQ|VTI|VOO|IWM|EEM|TLT|GLD|SLV|USO|XL[A-Z]|ARK[A-Z]|IV[A-Z])$/.test(s)) {
    return 'etf';
  }

  // Default: stocks (most common for unknown symbols)
  return 'stocks';
}

/**
 * Parse number from various European/US formats.
 * Handles: "1,234.56" (US), "1.234,56" (EU), "1234.56", etc.
 */
export function parseNumber(value: string | number | undefined | null): number | null {
  if (value === null || value === undefined) return null;
  if (typeof value === 'number') return isFinite(value) ? value : null;

  const str = String(value).trim().replace(/\s/g, '');
  if (!str) return null;

  // Detect format: if both , and . present, the rightmost is decimal
  const hasComma = str.includes(',');
  const hasDot = str.includes('.');

  let normalized: string;
  if (hasComma && hasDot) {
    const lastComma = str.lastIndexOf(',');
    const lastDot = str.lastIndexOf('.');
    if (lastComma > lastDot) {
      // EU format: 1.234,56
      normalized = str.replace(/\./g, '').replace(',', '.');
    } else {
      // US format: 1,234.56
      normalized = str.replace(/,/g, '');
    }
  } else if (hasComma) {
    // Could be EU decimal (1,5) or US thousands (1,234)
    const parts = str.split(',');
    if (parts.length === 2 && parts[1].length <= 2) {
      // Likely EU decimal
      normalized = str.replace(',', '.');
    } else {
      // US thousands
      normalized = str.replace(/,/g, '');
    }
  } else {
    normalized = str;
  }

  const num = parseFloat(normalized);
  return isFinite(num) ? num : null;
}

/**
 * Parse date from various formats
 */
export function parseDate(value: string | number | Date | undefined | null): Date | null {
  if (!value) return null;
  if (value instanceof Date) return isFinite(value.getTime()) ? value : null;

  if (typeof value === 'number') {
    // Excel serial date
    if (value > 25000 && value < 60000) {
      const d = new Date((value - 25569) * 86400 * 1000);
      return isFinite(d.getTime()) ? d : null;
    }
    // Unix timestamp
    const d = new Date(value > 1e10 ? value : value * 1000);
    return isFinite(d.getTime()) ? d : null;
  }

  const str = String(value).trim();

  // Try ISO first
  const isoDate = new Date(str);
  if (isFinite(isoDate.getTime())) return isoDate;

  // Try DD.MM.YYYY HH:MM:SS (common EU/RO format)
  const euMatch = str.match(
    /^(\d{1,2})[.\/\-](\d{1,2})[.\/\-](\d{2,4})(?:[\sT](\d{1,2}):(\d{2})(?::(\d{2}))?)?/
  );
  if (euMatch) {
    const [, dd, mm, yyyy, hh = '0', min = '0', ss = '0'] = euMatch;
    const year = yyyy.length === 2 ? 2000 + parseInt(yyyy) : parseInt(yyyy);
    const d = new Date(
      year,
      parseInt(mm) - 1,
      parseInt(dd),
      parseInt(hh),
      parseInt(min),
      parseInt(ss)
    );
    if (isFinite(d.getTime())) return d;
  }

  return null;
}
